﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GoodAI.Arnold.Communication;
using GoodAI.Arnold.Extensions;
using GoodAI.Logging;

namespace GoodAI.Arnold.Core
{
    public interface ICoreController : IDisposable
    {
        Task<StateResponse> Command(CommandConversation conversation, Func<TimeoutAction> timeoutCallback, bool restartKeepaliveOnSuccess = true, int timeoutMs = 0);

        bool IsCommandInProgress { get; }
        void StartStateChecking(Action<KeepaliveResult> stateResultAction);
    }

    public enum TimeoutAction
    {
        Wait,
        Retry,
        Cancel
    }

    // One value, just used to describe the constructor that sets RequestFailed to true.
    public enum KeepaliveResultTag
    {
        RequestFailed
    }

    public class KeepaliveResult
    {
        public KeepaliveResult(StateResponse stateResponse)
        {
            StateResponse = stateResponse;
        }

        public KeepaliveResult(KeepaliveResultTag result)
        {
            RequestFailed = true;
        }
        
        public bool RequestFailed { get; }
        public StateResponse StateResponse { get; }
    }

    public class CoreController : ICoreController
    {
        // Injected.
        public ILog Log { get; set; } = NullLogger.Instance;

        private readonly ICoreLink m_coreLink;
        private readonly int m_keepaliveIntervalMs;
        private Task<StateResponse> m_runningCommand;
        private Action<KeepaliveResult> m_stateResultAction;
        private CancellationTokenSource m_cancellationTokenSource;

        private const int CommandTimeoutMs = 15*1000;
        private const int DefaultKeepaliveIntervalMs = 500;
        private const int DefaultKeepaliveTimeoutMs = DefaultKeepaliveIntervalMs;

        public bool IsCommandInProgress => m_runningCommand != null;

        public CoreController(ICoreLink coreLink, int keepaliveIntervalMs = DefaultKeepaliveIntervalMs)
        {
            m_coreLink = coreLink;
            m_keepaliveIntervalMs = keepaliveIntervalMs;
            m_cancellationTokenSource = new CancellationTokenSource();
        }

        public void StartStateChecking(Action<KeepaliveResult> stateResultAction)
        {
            if (stateResultAction == null)
                throw new ArgumentNullException(nameof(stateResultAction));

            m_stateResultAction = stateResultAction;

            m_cancellationTokenSource.Cancel();
            m_cancellationTokenSource = new CancellationTokenSource();

            Task task = RepeatGetStateAsync(m_keepaliveIntervalMs, m_cancellationTokenSource);
        }

        private async Task RepeatGetStateAsync(int repeatMillis, CancellationTokenSource tokenSource)
        {
            while (true)
            {
                if (IsCommandInProgress)
                    continue;

                try
                {
                    // TODO(): Handle timeout and other exceptions here.
                    StateResponse stateCheckResult =
                        await m_coreLink.Request(new GetStateConversation(), DefaultKeepaliveTimeoutMs)
                            .ConfigureAwait(false);

                    // Check this again - the cancellation could have come during the request.
                    if (!tokenSource.IsCancellationRequested)
                        m_stateResultAction(new KeepaliveResult(stateCheckResult));
                }
                catch (Exception ex)
                {
                    // TODO(HonzaS): if this keeps on failing, notify the user.
                    Log.Warn("Periodic state check failed: {message}", ex.Message);
                    m_stateResultAction(new KeepaliveResult(KeepaliveResultTag.RequestFailed));
                }

                try
                {
                    await Task.Delay(repeatMillis, tokenSource.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (ex is TaskCanceledException)
                        return;

                    Log.Warn(ex, "Task.Delay threw an exception");
                }
            }
        }

        public void Dispose()
        {
            Log.Debug("Disposing");
            m_cancellationTokenSource.Cancel();
        }

        public async Task<StateResponse> Command(CommandConversation conversation, Func<TimeoutAction> timeoutCallback,
            bool restartKeepaliveOnSuccess = true, int timeoutMs = CommandTimeoutMs)
        {
            if (m_runningCommand != null)
            {
                CommandType commandType = conversation.RequestData.Command;
                Log.Info("A command is already running: {commandType}", commandType);
                throw new InvalidOperationException($"A command is already running {commandType}");
            }

            m_cancellationTokenSource.Cancel();

            var retry = false;

            StateResponse result = null;
            while (true)
            {
                try
                {
                    if (retry || m_runningCommand == null)
                    {
                        retry = false;
                        m_runningCommand = m_coreLink.Request(conversation, timeoutMs);
                    }

                    result = await m_runningCommand.ConfigureAwait(false);
                }
                catch (TaskTimeoutException<StateResponse> ex)
                {
                    TimeoutAction timeoutAction = timeoutCallback();
                    Log.Info("Command {command} timed out, {action} requested", conversation.RequestData.Command,
                        timeoutAction);
                    if (timeoutAction == TimeoutAction.Cancel)
                        break;

                    if (timeoutAction == TimeoutAction.Retry)
                        retry = true;

                    if (timeoutAction == TimeoutAction.Wait)
                        m_runningCommand = ex.OriginalTask.TimeoutAfter(CommandTimeoutMs);

                    continue;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Request failed");
                    m_runningCommand = null;
                    RestartStateChecking();
                    throw;
                }

                Log.Debug("Successful command {command}", conversation.RequestData.Command);
                break;
            }

            m_runningCommand = null;

            if (restartKeepaliveOnSuccess)
                RestartStateChecking();

            return result;
        }

        private void RestartStateChecking()
        {
            if (m_stateResultAction != null)
            {
                Log.Debug("Restarting regular state checking");
                StartStateChecking(m_stateResultAction);
            }
        }
    }
}
