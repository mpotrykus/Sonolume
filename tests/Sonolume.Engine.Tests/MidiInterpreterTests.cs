using Sonolume.Engine.Input;
using Xunit;

namespace Sonolume.Engine.Tests;

public class MidiInterpreterTests
{
    [Fact]
    public void NoteOn_BecomesTriggerWithVelocity()
    {
        var e = MidiEvent.NoteOn(0, 36, 0.5f, 123);
        Assert.True(MidiInterpreter.TryInterpret(e, out var c));
        Assert.Equal(ControlEventType.Trigger, c.Type);
        Assert.Equal(SourceKind.MidiNote, c.Source.Kind);
        Assert.Equal(36, c.Source.Number);
        Assert.Equal(0, c.Source.Channel);
        Assert.Equal(0.5f, c.Value01, 3);
        Assert.Equal(123, c.TimestampTicks);
    }

    [Fact]
    public void NoteOnWithZeroVelocity_BecomesRelease()
    {
        Assert.True(MidiInterpreter.TryInterpret(MidiEvent.NoteOn(0, 36, 0f, 0), out var c));
        Assert.Equal(ControlEventType.Release, c.Type);
    }

    [Fact]
    public void NoteOff_BecomesRelease()
    {
        Assert.True(MidiInterpreter.TryInterpret(MidiEvent.NoteOff(3, 40, 0.2f, 0), out var c));
        Assert.Equal(ControlEventType.Release, c.Type);
        Assert.Equal(3, c.Source.Channel);
    }

    [Fact]
    public void ControlChange_BecomesSet()
    {
        Assert.True(MidiInterpreter.TryInterpret(MidiEvent.ControlChange(0, 7, 127, 0), out var c));
        Assert.Equal(ControlEventType.Set, c.Type);
        Assert.Equal(SourceKind.MidiCC, c.Source.Kind);
        Assert.Equal(7, c.Source.Number);
        Assert.Equal(1f, c.Value01, 4);
    }

    [Fact]
    public void PitchBend_UsesFull14Bits()
    {
        Assert.True(MidiInterpreter.TryInterpret(MidiEvent.PitchBend(0, 8192, 0), out var c));
        Assert.Equal(SourceKind.MidiPitchBend, c.Source.Kind);
        Assert.Equal(0.5f, c.Value01, 3);
    }

    [Fact]
    public void SevenBitConversion_MapsExtremesExactly()
    {
        Assert.Equal(0, MidiEvent.From7Bit(0));
        Assert.Equal(MidiEvent.MaxValue14, MidiEvent.From7Bit(127));
    }

    [Fact]
    public void SourceAddress_WildcardsMatch()
    {
        var any = SourceAddress.Note(36);
        Assert.True(any.Matches(new SourceAddress(SourceKind.MidiNote, 9, 36)));
        Assert.False(any.Matches(new SourceAddress(SourceKind.MidiNote, 9, 37)));
        Assert.False(any.Matches(new SourceAddress(SourceKind.MidiCC, 9, 36)));

        var channel = SourceAddress.Note(36, channel: 2);
        Assert.True(channel.Matches(new SourceAddress(SourceKind.MidiNote, 2, 36)));
        Assert.False(channel.Matches(new SourceAddress(SourceKind.MidiNote, 3, 36)));
    }
}
