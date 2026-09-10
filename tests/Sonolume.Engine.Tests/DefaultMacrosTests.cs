using System.Linq;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Xunit;

namespace Sonolume.Engine.Tests;

public class DefaultMacrosTests
{
    [Fact]
    public void Sync_ReplacesStaleHostMacroMappingsWithCurrentConvention()
    {
        var project = Project.CreateEmpty();
        project.AddZone(new Zone { Id = "strip", Name = "Strip" });

        // Simulate a project saved under an earlier macro convention (e.g. slot 0 driving Hue directly, no
        // Select-mode Effect Type/Blend slots) that predates today's fixed layout.
        project.Mappings.Add(new Mapping
        {
            Id = "stale-macro-0",
            Source = SourceAddress.HostMacro(0),
            Target = TargetRef.Zone("strip"),
            Param = ParamId.Hue,
            Mode = MappingMode.Set,
        });

        DefaultMacros.Sync(project);

        var hostMacros = project.Mappings.Where(m => m.Source.Kind == SourceKind.HostMacro).ToList();
        Assert.DoesNotContain(hostMacros, m => m.Id == "stale-macro-0");

        var slot0 = hostMacros.Single(m => m.Source.Number == DefaultMacros.EffectTypeSlot);
        Assert.Equal(MappingMode.Select, slot0.Mode);

        var hueSlot = hostMacros.Single(m => m.Param == ParamId.Hue);
        Assert.Equal(DefaultMacros.SlotFor(ParamId.Hue), hueSlot.Source.Number);
        Assert.Equal(MappingMode.Set, hueSlot.Mode);
    }
}
