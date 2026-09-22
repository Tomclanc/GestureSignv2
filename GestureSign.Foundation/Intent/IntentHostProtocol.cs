namespace GestureSign.Foundation.Intent;

public sealed record IntentHostRequest(string Command, IntentMode Mode = IntentMode.Off, string? SampleId = null, IntentLabel Label = IntentLabel.Unknown);
public sealed record IntentSampleSummary(string Id, DateTimeOffset CreatedUtc, IntentLabel Label, string? Candidate, float? Score, string? Backend, bool Blocked);
public sealed class IntentHostResponse
{
    public int Protocol { get; set; } = 2;
    public string? Error { get; set; }
    public bool Busy { get; set; }
    public bool BackgroundLearning { get; set; }
    public bool Eligible { get; set; }
    public string Message { get; set; } = "";
    public string Backend { get; set; } = "未训练";
    public IntentControl Control { get; set; } = new();
    public int Scrolls { get; set; }
    public int Gestures { get; set; }
    public int Unknown { get; set; }
    public IntentSampleSummary[] Samples { get; set; } = [];
    public IntentSample? Sample { get; set; }
}
