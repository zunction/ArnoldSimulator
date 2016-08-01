﻿#include "gen_spec_input_neuron.h"

GenSpecInputNeuron::GenSpecInputNeuron(NeuronBase &base, json &params) : Neuron(base, params)
{
    mInputSizeX = params["inputSizeX"].get<size_t>();
    mInputSizeY = params["inputSizeY"].get<size_t>();
    mNeuronInputSizeX = params["neuronInputSizeX"].get<size_t>();
    mNeuronInputSizeY = params["neuronInputSizeY"].get<size_t>();
    mNeuronInputStrideX = params["neuronInputStrideX"].get<size_t>();
    mNeuronInputStrideY = params["neuronInputStrideY"].get<size_t>();
    mNeuronCountX = params["neuronCountX"].get<size_t>();
    mNeuronCountY = params["neuronCountY"].get<size_t>();
}

GenSpecInputNeuron::~GenSpecInputNeuron()
{
}

void GenSpecInputNeuron::pup(PUP::er &p)
{
}

const char *GenSpecInputNeuron::Type = "GenSpecInputNeuron";

const char *GenSpecInputNeuron::GetType() const
{
    return Type;
}

void GenSpecInputNeuron::HandleSpike(Direction direction, MultiByteSpike &spike, Spike::Data &spikeData)
{
    int i = 0;
    const NeuronBase::Synapses &outputSynapses = mBase.GetOutputSynapses();

    const uint8_t *spikeValues = spike.GetValues(spikeData);

    for (const auto &entry : outputSynapses) {
        NeuronId generalist = entry.first;

        // Get neuron coordinates in the "neuron grid".
        int neuronX = i % mNeuronCountX;
        int neuronY = i / mNeuronCountX;

        // Get starting X position in the input grid.
        int fromX = neuronX * mNeuronInputStrideX;

        // Get starting and ending Y position in the input grid.
        int fromY = neuronY * mNeuronInputStrideY;
        int toY = fromY + mNeuronInputSizeY;

        // Storage for the resulting data.
        size_t neuronValuesCount = mNeuronInputSizeX * mNeuronInputSizeY;
        std::unique_ptr<uint8_t[]> neuronValuesPtr(new uint8_t[neuronValuesCount]);
        uint8_t *neuronValues = neuronValuesPtr.get();

        for (int y = fromY; y < toY; y++) {
            // Get current Y and X values
            size_t fromDataStart = y * mInputSizeX + fromX;
            size_t toDataStart = (y - fromY) * mNeuronInputSizeX;
            for (int x = 0; x < mNeuronInputSizeX; x++) {
                // Put them into the spike, 1 and 0 values only.
                neuronValues[toDataStart + x] = spikeValues[fromDataStart + x] > 0 ? 1 : 0;
            }
            //std::memcpy(neuronValues + toDataStart, spikeValues + fromDataStart, mNeuronInputSizeX);
        }

        SendMultiByteSpike(Direction::Forward, generalist, neuronValues, neuronValuesCount);

        i++;
    }
}

void GenSpecInputNeuron::Control(size_t brainStep)
{
}

size_t GenSpecInputNeuron::ContributeToRegion(uint8_t *&contribution)
{
    return 0;
}

void GenSpecInputNeuron::SendMultiByteSpike(Direction direction, NeuronId receiver, uint8_t *values, size_t count)
{
    Spike::Data data;
    Spike::Initialize(Spike::Type::MultiByte, mBase.GetId(), data);
    MultiByteSpike *spike = static_cast<MultiByteSpike *>(Spike::Edit(data));
    spike->SetValues(data, values, count);

    mBase.SendSpike(receiver, direction, data);
}

