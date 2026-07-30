using SentinelX.Core.Models;

namespace SentinelX.Infrastructure.Monitoring;

/// <summary>
/// Wraps a <see cref="MetricSample"/> for delivery over
/// <see cref="CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger"/>, decoupling engines
/// from whichever ViewModels are currently listening.
/// </summary>
public sealed class MetricSampleMessage
{
    public MetricSampleMessage(MetricSample sample)
    {
        Sample = sample;
    }

    public MetricSample Sample { get; }
}
