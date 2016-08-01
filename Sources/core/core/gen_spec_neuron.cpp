#include "gen_spec_neuron.h"
#include "log.h"
#include <numeric>

GenSpecNeuron::GenSpecNeuron(NeuronBase &base, json &params) : Neuron(base, params),
    mResult(0), mPreviousResult(0), mSynapseThreshold(0.5), mIsWinner(false),
    mChildrenAnswered(0), mBestChild(0), mBestChildResult(0), mGenValueLimit(1),
    mActiveInputsVsRegisteredInputs(0), mGenFactor(0)
{
    if (params.empty()) {
        Log(LogLevel::Error, "GenSpecNeuron: Parameters not received");
        return;
    }

    mSynapseThreshold = params["synapseThreshold"].get<double>();
    auto inputSizeX = params["inputSizeX"].get<size_t>();
    auto inputSizeY = params["inputSizeY"].get<size_t>();
    mInputSize = inputSizeX * inputSizeY;

    mLastInputPtr = std::unique_ptr<uint8_t[]>(new uint8_t[mInputSize]);
    // Fill with zeros for input difference calculation.
    std::memset(mLastInputPtr.get(), 0, mInputSize);

    const json &accumulatorId = params["accumulatorId"];
    // accumulatorId would be empty for the generalist that is parent of the first layer.
    if (!accumulatorId.empty())
        mAccumulatorId = params["accumulatorId"].get<NeuronId>();

    const json genValueCount = params["generalizationValueCountLimit"];
    if (!genValueCount.empty())
        mGenValueLimit = genValueCount.get<size_t>();
}

GenSpecNeuron::~GenSpecNeuron()
{
}

void GenSpecNeuron::pup(PUP::er &p)
{
    // TODO
}

const char *GenSpecNeuron::Type = "GenSpecNeuron";

const char *GenSpecNeuron::GetType() const
{
    return Type;
}

void GenSpecNeuron::HandleSpike(Direction direction, MultiByteSpike &spike, Spike::Data &spikeData)
{
    // Forward pass - propagation of output->input.
    Log(LogLevel::Info, "Neuron #%d received input", mBase.GetIndex());

    NeuronId sender = Spike::GetSender(spikeData);

    Synapse::Data *synapseData;

    synapseData = mBase.AccessInputSynapse(sender);
    MultiWeightedSynapse *synapse = static_cast<MultiWeightedSynapse *>(Synapse::Edit(*synapseData));

    uint16_t weightCount = synapse->GetWeightCount(*synapseData);
    std::unique_ptr<float[]> weights(new float[weightCount]);
    synapse->GetWeights(*synapseData, weights.get(), weightCount);

    uint8_t *values = mLastInputPtr.get();
    spike.GetValues(spikeData, values, weightCount);

    size_t activatedInputs = 0;
    for (int i = 0; i < weightCount; i++) {
        mResult += ((weights[i] > mSynapseThreshold) ? values[i] : 0);
        activatedInputs += weights[i];
    }

    // Get generalization factor.
    mActiveInputsVsRegisteredInputs = float(activatedInputs) / float(mResult);

    Functions::ResultArgs args;
    args.result = mResult;
    SendFunctionalSpike(Direction::Backward, mBase.GetParent(), Functions::Function::Result, args);
}

void GenSpecNeuron::HandleSpike(Direction direction, FunctionalSpike &spike, Spike::Data &spikeData)
{
    switch (static_cast<Functions::Function>(spike.GetFunction(spikeData))) {
    case Functions::Function::Result:
    {
        Log(LogLevel::Info, "Neuron #%d received child results", mBase.GetIndex());
        // A child has sent it's result for winner determination.
        Functions::ResultArgs args;
        spike.GetArguments(spikeData, &args, sizeof(Functions::ResultArgs));

        if (args.result > mBestChildResult) {
            mBestChildResult = args.result;
            mBestChild = Spike::GetSender(spikeData);
        }

        mChildrenAnswered++;

        auto childrenCount = mBase.GetChildren().size();
        // mBestChild is 0 when no children answered with a higher-than-zero result.
        if (childrenCount > 0 && mChildrenAnswered == childrenCount && mBestChild != 0) {
            // Received results from all children, select winner and notify him.

            Functions::WinnerSelectedArgs winnerArgs;
            winnerArgs.winner = mBestChild;
            for (auto child : mBase.GetChildren()) {
                SendFunctionalSpike(Direction::Forward, child, Functions::Function::WinnerSelected, winnerArgs);
            }

            mBestChildResult = 0;
            mChildrenAnswered = 0;
            mBestChild = 0;
        }

        break;
    }
    case Functions::Function::WinnerSelected:
    {
        Log(LogLevel::Info, "Neuron #%d received winner info", mBase.GetIndex());

        Functions::WinnerSelectedArgs winnerArgs;
        spike.GetArguments(spikeData, &winnerArgs, sizeof(Functions::WinnerSelectedArgs));

        size_t result = 0;

        if (winnerArgs.winner == mBase.GetId()) {
            Log(LogLevel::Info, "Neuron #%d is winner", mBase.GetIndex());
            // This neuron is "activated".
            result = 1;

            mIsWinner = true;
            UpdateWeights();

            for (const NeuronId &child : mBase.GetChildren()) {
                SendMultiByteSpike(Direction::Forward, child, mLastInputPtr.get(), mInputSize);
            }

            // Send activation data to the accumulator.
            Functions::ResultArgs args;
            args.result = result;
            args.isLeaf = mBase.GetChildren().empty();
            SendFunctionalSpike(Direction::Forward, mAccumulatorId, Functions::Function::Result, args);
        }

        // The name was too long for the following lines.
        auto &gen = mGeneralizationValues;

        // If this neuron didn't win, a zero is used in the average to bring down
        // its generalization.
        gen.push_back(mActiveInputsVsRegisteredInputs * result);
        if (gen.size() > mGenValueLimit)
            gen.pop_front();

        mGenFactor = std::accumulate(gen.begin(), gen.end(), 0.0);
        mGenFactor /= gen.size();

        break;
    }
    default:
        break;
    }
}

void GenSpecNeuron::UpdateWeights()
{
}

float GenSpecNeuron::GetInputDifference(uint8_t *oldInput, uint8_t *newInput)
{
    size_t differences = 0;
    for (int i = 0; i < mInputSize; i++) {
        differences += std::abs(oldInput[i] - newInput[i]);
    }

    return static_cast<float>(differences) / static_cast<float>(mInputSize);
}

void GenSpecNeuron::CalculateObserver(ObserverType type, std::vector<int32_t> &metadata, std::vector<uint8_t> &observerData)
{
    // TODO(HonzaS): Use composition here, provide observer classes.
    if (type == ObserverType::FloatTensor) {
        //observerData.push_back(mAccumulatedActivation * 255 / (100 * mGeneralistActivation));
    }
}

void GenSpecNeuron::Control(size_t brainStep)
{
    // The neuron is only reactive, everything is handled in HandleSpike methods.
    // The only accumulation happens when children send their results and the reaction is
    // done immediatelly when the last message arrives.
}

size_t GenSpecNeuron::ContributeToRegion(uint8_t *&contribution)
{
    size_t size = sizeof(float) + 1;
    contribution = new uint8_t[size];
    std::memcpy(contribution, &mGenFactor, sizeof(float));
    const auto &children = mBase.GetChildren();
    size_t sz = children.size();
    bool empty = children.empty();
    contribution[size - 1] = (mBase.GetChildren().empty()) ? 1 : 0;

    return size;
}

void GenSpecNeuron::SendMultiByteSpike(Direction direction, NeuronId receiver, uint8_t *values, size_t count)
{
    Spike::Data data;
    Spike::Initialize(Spike::Type::MultiByte, mBase.GetId(), data);
    MultiByteSpike *spike = static_cast<MultiByteSpike *>(Spike::Edit(data));
    spike->SetValues(data, values, count);

    mBase.SendSpike(receiver, direction, data);
}

void GenSpecNeuron::SendDiscreteSpike(Direction direction, NeuronId receiver, uint64_t value)
{
    Spike::Data data;
    Spike::Initialize(Spike::Type::Discrete, mBase.GetId(), data);
    DiscreteSpike *spike = static_cast<DiscreteSpike *>(Spike::Edit(data));
    spike->SetIntensity(data, value);

    mBase.SendSpike(receiver, direction, data);
}

