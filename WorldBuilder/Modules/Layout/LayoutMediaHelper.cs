using DatReaderWriter.Types;
using System.Collections.ObjectModel;
using System.Linq;

namespace WorldBuilder.Modules.Layout;

public static class LayoutMediaHelper {

    public static uint NormalizeSurfaceId(uint id) {
        if (id == 0) return 0;
        if ((id & 0xFFFF0000u) == 0 && id <= 0xFFFFu)
            return id | 0x08000000u;
        return id;
    }

    public static uint? TryFirstImageSurfaceId(StateDesc? state) {
        if (state?.Media == null) return null;
        foreach (var m in state.Media) {
            if (m is MediaDescImage img) {
                var n = NormalizeSurfaceId(img.File);
                return n == 0 ? null : n;
            }
        }
        return null;
    }

    public static uint? TryPrimarySurfaceForElement(ElementDesc el) {
        if (el.States != null && el.States.TryGetValue(el.DefaultState, out var defState)) {
            var s = TryFirstImageSurfaceId(defState);
            if (s.HasValue) return s;
        }

        var fromTemplate = TryFirstImageSurfaceId(el.StateDesc);
        if (fromTemplate.HasValue) return fromTemplate;

        if (el.States != null) {
            foreach (var kv in el.States.OrderBy(k => (uint)k.Key)) {
                var s = TryFirstImageSurfaceId(kv.Value);
                if (s.HasValue) return s;
            }
        }

        return null;
    }

    public static void PopulateStateRows(ElementDesc el, ObservableCollection<LayoutStateRow> rows) {
        rows.Clear();
        rows.Add(MakeRow("Template", el.StateDesc ?? new StateDesc()));
        if (el.States == null) return;
        foreach (var kv in el.States.OrderBy(k => (uint)k.Key)) {
            rows.Add(MakeRow($"UIState 0x{(uint)kv.Key:X8}", kv.Value));
        }
    }

    static LayoutStateRow MakeRow(string label, StateDesc sd) {
        var surf = TryFirstImageSurfaceId(sd);
        string mediaSummary = "—";
        if (sd.Media is { Count: > 0 }) {
            mediaSummary = string.Join(", ",
                sd.Media.Select(m => m.GetType().Name.Replace("MediaDesc", "", StringComparison.Ordinal)));
        }

        return new LayoutStateRow {
            RowLabel = label,
            StateRecordIdHex = $"0x{sd.StateId:X8}",
            FirstImageSurfaceHex = surf.HasValue ? $"0x{surf.Value:X8}" : null,
            MediaSummary = mediaSummary
        };
    }
}

public class LayoutStateRow {
    public string RowLabel { get; init; } = "";
    public string StateRecordIdHex { get; init; } = "";
    public string? FirstImageSurfaceHex { get; init; }
    public string FirstImageSurfaceDisplay => FirstImageSurfaceHex ?? "—";
    public string MediaSummary { get; init; } = "";
}
