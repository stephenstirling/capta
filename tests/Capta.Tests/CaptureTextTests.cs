using Capta.Services;

namespace Capta.Tests;

/// <summary>"Save text found in captures": off means no OCR runs for the file and no text is written.</summary>
public class CaptureTextTests
{
    [Fact]
    public async Task WhenOffTheRecogniserIsNeverCalled()
    {
        var called = false;
        var text = await CaptureText.ForMetadataAsync(save: false, () => { called = true; return Task.FromResult<string?>("secret"); });

        Assert.Null(text);
        Assert.False(called);
    }

    [Fact]
    public async Task WhenOnTheRecognisedTextIsReturned()
    {
        var text = await CaptureText.ForMetadataAsync(save: true, () => Task.FromResult<string?>("Network\r\nWi-Fi"));
        Assert.Equal("Network\r\nWi-Fi", text);
    }

    [Fact]
    public async Task WhenOnButNothingIsFoundThereIsNoText()
    {
        Assert.Null(await CaptureText.ForMetadataAsync(save: true, () => Task.FromResult<string?>(null)));
    }

    [Fact]
    public async Task AFailingRecogniserStillAllowsTheCaptureToBeSaved()
    {
        var text = await CaptureText.ForMetadataAsync(save: true, () => throw new InvalidOperationException("no OCR language"));
        Assert.Null(text);

        var faulted = await CaptureText.ForMetadataAsync(save: true, async () => { await Task.Yield(); throw new IOException("boom"); });
        Assert.Null(faulted);
    }
}
