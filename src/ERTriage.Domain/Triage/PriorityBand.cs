namespace ERTriage.Domain.Triage;

/// <summary>
/// One classification band for a vital sign: values in [<paramref name="From"/>, <paramref name="To"/>)
/// map to <paramref name="Priority"/>. <paramref name="Description"/> is used in the reason text.
/// </summary>
public readonly record struct PriorityBand(int Priority, decimal From, decimal To, string Description)
{
    public bool Contains(decimal value) => value >= From && value < To;
}
