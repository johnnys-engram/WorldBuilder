using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib;
using DatReaderWriter.Lib.IO;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

[MemoryPackable]
public partial class LayoutDatData {
    public Dictionary<uint, LayoutDatEntry> Entries = new();
}

[MemoryPackable]
public partial class LayoutDatEntry {
    public string TypeName = "";
    public byte[] Data = Array.Empty<byte>();
}

/// <summary>
/// Project-side overrides for <see cref="LayoutDesc"/>; export writes them via <see cref="IDatReaderWriter.TrySave{T}"/>.
/// </summary>
[MemoryPackable]
public partial class LayoutDatDocument : BaseDocument {
    private const int PackBufferSize = 32 * 1024 * 1024;
    private static readonly ILogger<LayoutDatDocument> Log = NullLogger<LayoutDatDocument>.Instance;

    public const string DocumentId = "ui_layouts";

    [MemoryPackOrder(10)]
    public LayoutDatData Overlay { get; set; } = new();

    [MemoryPackIgnore]
    private readonly Dictionary<uint, object> _objectCache = new();

    [MemoryPackIgnore]
    private DatDatabase? _unpackContext;

    [MemoryPackConstructor]
    public LayoutDatDocument() : base(DocumentId) {
    }

    public LayoutDatDocument(string id) : base(id) {
        if (id != DocumentId) {
            throw new ArgumentException($"Id must be {DocumentId}", nameof(id));
        }
    }

    public int EntryCount => Overlay.Entries.Count;

    public bool HasStoredLayout(uint layoutId) => Overlay.Entries.ContainsKey(layoutId);

    public IEnumerable<uint> GetLayoutIds() => Overlay.Entries.Keys;

    public void SetLayout(uint layoutId, LayoutDesc layout) {
        layout.Id = layoutId;
        _objectCache[layoutId] = layout;

        try {
            var buffer = new byte[PackBufferSize];
            var writer = new DatBinWriter(buffer.AsMemory());
            ((IPackable)layout).Pack(writer);
            Overlay.Entries[layoutId] = new LayoutDatEntry {
                TypeName = nameof(LayoutDesc),
                Data = buffer[..writer.Offset]
            };
        }
        catch (Exception ex) {
            Log.LogError(ex, "[LayoutDatDoc] Failed to pack layout 0x{Id:X8}", layoutId);
            Overlay.Entries[layoutId] = new LayoutDatEntry {
                TypeName = nameof(LayoutDesc),
                Data = Array.Empty<byte>()
            };
        }
    }

    public bool TryGetLayout(uint layoutId, out LayoutDesc? layout) {
        if (_objectCache.TryGetValue(layoutId, out var cached) && cached is LayoutDesc typed) {
            layout = typed;
            return true;
        }

        if (Overlay.Entries.TryGetValue(layoutId, out var entry) && entry.Data.Length > 0) {
            if (_unpackContext == null) {
                Log.LogError("[LayoutDatDoc] Cannot unpack layout 0x{Id:X8}: unpack context not initialized", layoutId);
            }
            else {
                try {
                    var obj = new LayoutDesc();
                    var reader = new DatBinReader(entry.Data.AsMemory(), _unpackContext);
                    ((IUnpackable)obj).Unpack(reader);
                    obj.Id = layoutId;
                    _objectCache[layoutId] = obj;
                    layout = obj;
                    return true;
                }
                catch (Exception ex) {
                    Log.LogError(ex, "[LayoutDatDoc] Failed to unpack layout 0x{Id:X8}", layoutId);
                }
            }
        }

        layout = default;
        return false;
    }

    public void RemoveLayout(uint layoutId) {
        Overlay.Entries.Remove(layoutId);
        _objectCache.Remove(layoutId);
    }

    public override Task InitializeForUpdatingAsync(IDatReaderWriter dats, IDocumentManager documentManager, ITransaction? tx, CancellationToken ct) {
        _unpackContext = dats.Language.Db;
        return Task.CompletedTask;
    }

    public override Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager, ITransaction? tx, CancellationToken ct) {
        _unpackContext = dats.Language.Db;
        return Task.CompletedTask;
    }

    protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int portalIteration = 0, int cellIteration = 0, IProgress<float>? progress = null) {
        SyncCacheToOverlay();

        foreach (var (layoutId, entry) in Overlay.Entries) {
            bool saved = false;

            if (_objectCache.TryGetValue(layoutId, out var cachedObj) && cachedObj is LayoutDesc live) {
                live.Id = layoutId;
                saved = datwriter.TrySave(live, portalIteration);
            }

            if (!saved && entry.Data.Length > 0) {
                saved = UnpackAndSave(datwriter, entry.Data, layoutId, portalIteration);
            }

            if (saved) {
                Log.LogInformation("[LayoutDatDoc] Exported layout 0x{Id:X8}", layoutId);
            }
            else if (entry.Data.Length > 0) {
                Log.LogError("[LayoutDatDoc] Failed to export layout 0x{Id:X8}", layoutId);
            }
        }

        return Task.FromResult(true);
    }

    private void SyncCacheToOverlay() {
        foreach (var (layoutId, obj) in _objectCache) {
            if (obj is not LayoutDesc live) continue;
            if (!Overlay.Entries.TryGetValue(layoutId, out var entry)) continue;
            try {
                var buffer = new byte[PackBufferSize];
                var writer = new DatBinWriter(buffer.AsMemory());
                live.Id = layoutId;
                ((IPackable)live).Pack(writer);
                entry.Data = buffer[..writer.Offset];
            }
            catch (Exception ex) {
                Log.LogError(ex, "[LayoutDatDoc] Re-pack failed for 0x{Id:X8}", layoutId);
            }
        }
    }

    private static bool UnpackAndSave(IDatReaderWriter writer, byte[] data, uint layoutId, int iteration) {
        var local = writer.Language.Db;
        var obj = new LayoutDesc();
        var reader = new DatBinReader(data.AsMemory(), local);
        ((IUnpackable)obj).Unpack(reader);
        obj.Id = layoutId;
        return writer.TrySave(obj, iteration);
    }

    public override void Dispose() {
        _objectCache.Clear();
        Overlay.Entries.Clear();
    }
}
