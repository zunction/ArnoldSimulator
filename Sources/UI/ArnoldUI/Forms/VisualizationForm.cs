﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using GoodAI.Arnold.Visualization;
using GoodAI.Arnold.Properties;
using OpenTK;
using OpenTK.Input;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;

namespace GoodAI.Arnold.Forms
{
    public partial class VisualizationForm : Form
    {
        public const float MouseSlowFactor = 2;

        private readonly Stopwatch m_stopwatch = new Stopwatch();

        private InputInfo m_inputInfo;

        private bool m_mouseCaptured;
        private Vector2 m_lastMousePosition;

        private readonly Scene m_scene;

        public VisualizationForm(UIMain uiMain)
        {
            InitializeComponent();

            m_scene = new Scene(glControl, uiMain);
        }

        // Resize the glControl
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            glControl.Resize += glControl_Resize;
            glControl.MouseUp += glControl_MouseUp;

            m_scene.Init();

            Application.Idle += Application_Idle;

            glControl_Resize(glControl, EventArgs.Empty);

            glControl.Context.SwapInterval = 1;


            m_stopwatch.Start();
        }

        private void glControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                m_mouseCaptured = !m_mouseCaptured;
                
                if (m_mouseCaptured)
                    HideCursor();
                else
                    ShowCursor();
            }


            if (m_mouseCaptured)
                ResetLastMousePosition();

            if (!m_mouseCaptured && e.Button == MouseButtons.Left)
                m_scene.PickObject(e.X, glControl.Size.Height - e.Y);  // Invert Y (windows 0,0 is top left, GL is bottom left).
        }

        private void ResetLastMousePosition()
        {
            MouseState state = Mouse.GetState();
            m_lastMousePosition = new Vector2(state.X, state.Y);
        }

        private void HandleKeyboard()
        {
            if (!glControl.Focused)
                return;

            KeyboardState keyboardState = Keyboard.GetState();

            m_inputInfo.ShouldStop = keyboardState.IsKeyDown(Key.Escape);

            m_inputInfo.KeyLeft = keyboardState.IsKeyDown(Key.A);
            m_inputInfo.KeyRight = keyboardState.IsKeyDown(Key.D);
            m_inputInfo.KeyForward = keyboardState.IsKeyDown(Key.W);
            m_inputInfo.KeyBack = keyboardState.IsKeyDown(Key.S);
            m_inputInfo.KeyUp = keyboardState.IsKeyDown(Key.Space);
            m_inputInfo.KeyDown = keyboardState.IsKeyDown(Key.C);

            // Ctrl doesn't work with the above methods.
            m_inputInfo.KeySlow = Keyboard.GetState().IsKeyDown(Key.ControlLeft);
        }

        private void Stop()
        {
            Close();
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            while (!glControl.IsDisposed && glControl.IsIdle)
                Step();
        }

        private void Step()
        {
            m_stopwatch.Stop();
            float elapsedMs = m_stopwatch.ElapsedMilliseconds;
            m_stopwatch.Reset();
            m_stopwatch.Start();

            ResetInput();

            if (m_mouseCaptured)
            {
                Vector2 delta = (m_lastMousePosition - new Vector2(Mouse.GetState().X, Mouse.GetState().Y))/MouseSlowFactor;
                m_lastMousePosition += delta;

                m_inputInfo.CameraRotated = delta != Vector2.Zero;
                m_inputInfo.CameraDeltaX = delta.X;
                m_inputInfo.CameraDeltaY = delta.Y;

                Mouse.SetPosition(Left + glControl.Size.Width / 2, Top + glControl.Size.Height / 2);
                m_lastMousePosition = new Vector2(Mouse.GetState().X, Mouse.GetState().Y);
            }

            HandleKeyboard();

            if (m_inputInfo.ShouldStop)
            {
                Stop();
                return;
            }

            m_scene.Step(m_inputInfo, elapsedMs);
        }

        private void ResetInput()
        {
            m_inputInfo = new InputInfo();
        }

        private void glControl_Resize(object sender, EventArgs e)
        {
            GLControl c = sender as GLControl;

            if (c.Size.Height == 0)
                c.Size = new Size(c.Size.Width, 1);
        }

        private void HideCursor()
        {
            Cursor = new Cursor(Resources.EmptyCursor.Handle);
        }

        private void ShowCursor()
        {
            Cursor = Cursors.Default;
        }
    }
}
