using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

[MemoryPackable]
public partial class PortalDatData {
    public Dictionary<uint, PortalDatEntry> Entries = new();
}

[MemoryPackable]
public partial class PortalDatEntry {
    public string TypeName = "";
    public byte[] Data = Array.Empty<byte>();
}

/// <summary>
/// Stores modified portal DAT table entries (SpellTable, etc.) in the project;
/// export writes them into client_portal.dat.
/// </summary>
[MemoryPackable]
public partial class PortalDatDocument : BaseDocument {
    private const int PackBufferSize = 16 * 1024 * 1024;
    private static readonly ILogger<PortalDatDocument> Log = NullLogger<PortalDatDocument>.Instance;

    public const string DocumentId = "portal_tables";

    [MemoryPackOrder(10)]
    public PortalDatData Overlay { get; set; } = new();

    [MemoryPackIgnore]
    private readonly Dictionary<uint, object> _objectCache = new();

    [MemoryPackIgnore]
    private readonly HashSet<uint> _unpackFailures = new();

    [MemoryPackConstructor]
    public PortalDatDocument() : base(DocumentId) {
    }

    public PortalDatDocument(string id) : base(id) {
        if (id != DocumentId) {
            throw new ArgumentException($"Id must be {DocumentId}", nameof(id));
        }
    }

    public bool HasEntry(uint fileId) =>
        _objectCache.ContainsKey(fileId) || Overlay.Entries.ContainsKey(fileId);

    public int EntryCount => Overlay.Entries.Count;

    public IEnumerable<uint> GetEntryIds() => Overlay.Entries.Keys;

    public void SetEntry<T>(uint fileId, T obj) where T : IDBObj, new() {
        _objectCache[fileId] = obj;

        try {
            var buffer = new byte[PackBufferSize];
            var writer = new DatBinWriter(buffer.AsMemory());
            ((IPackable)obj).Pack(writer);
            Overlay.Entries[fileId] = new PortalDatEntry {
                TypeName = typeof(T).Name,
                Data = buffer[..writer.Offset]
            };
        }
        catch (Exception ex) {
            Log.LogError(ex, "[PortalDatDoc] Failed to pack entry 0x{FileId:X8}", fileId);
            Overlay.Entries[fileId] = new PortalDatEntry {
                TypeName = typeof(T).Name,
                Data = Array.Empty<byte>()
            };
        }
    }

    public bool TryGetEntry<T>(uint fileId, out T? obj) where T : IDBObj, new() {
        if (_objectCache.TryGetValue(fileId, out var cached) && cached is T typed) {
            obj = typed;
            return true;
        }

        if (_unpackFailures.Contains(fileId)) {
            obj = default;
            return false;
        }

        if (Overlay.Entries.TryGetValue(fileId, out var entry) && entry.Data.Length > 0) {
            try {
                var unpacked = new T();
                var reader = new DatBinReader(entry.Data);
                ((IUnpackable)unpacked).Unpack(reader);
                _objectCache[fileId] = unpacked;
                obj = unpacked;
                return true;
            }
            catch (Exception ex) {
                Log.LogError(ex, "[PortalDatDoc] Failed to unpack entry 0x{FileId:X8} (will not retry)", fileId);
                _unpackFailures.Add(fileId);
            }
        }

        obj = default;
        return false;
    }

    public void RemoveEntry(uint fileId) {
        Overlay.Entries.Remove(fileId);
        _objectCache.Remove(fileId);
        _unpackFailures.Remove(fileId);
    }

    public override Task InitializeForUpdatingAsync(IDatReaderWriter dats, IDocumentManager documentManager, ITransaction? tx, CancellationToken ct) {
        return Task.CompletedTask;
    }

    public override Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager, ITransaction? tx, CancellationToken ct) {
        return Task.CompletedTask;
    }

    protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int portalIteration = 0, int cellIteration = 0, IProgress<float>? progress = null) {
        SyncCacheToOverlay();

        foreach (var (fileId, entry) in Overlay.Entries) {
            bool saved = false;

            if (_objectCache.TryGetValue(fileId, out var cachedObj)) {
                saved = TrySaveTyped(datwriter, cachedObj, portalIteration);
            }

            if (!saved && entry.Data.Length > 0) {
                saved = TrySaveFromBytes(datwriter, entry, portalIteration);
            }

            if (saved) {
                Log.LogInformation("[PortalDatDoc] Exported 0x{FileId:X8} ({Type})", fileId, entry.TypeName);
            }
            else {
                Log.LogError("[PortalDatDoc] Failed to export 0x{FileId:X8} ({Type})", fileId, entry.TypeName);
            }
        }

        return Task.FromResult(true);
    }

    private void SyncCacheToOverlay() {
        foreach (var (fileId, obj) in _objectCache) {
            if (!Overlay.Entries.TryGetValue(fileId, out var entry)) continue;
            try {
                var buffer = new byte[PackBufferSize];
                var writer = new DatBinWriter(buffer.AsMemory());
                ((IPackable)obj).Pack(writer);
                entry.Data = buffer[..writer.Offset];
            }
            catch (Exception ex) {
                Log.LogError(ex, "[PortalDatDoc] Failed to re-pack entry 0x{FileId:X8} during sync", fileId);
            }
        }
    }

    private static bool TrySaveTyped(IDatReaderWriter writer, object obj, int iteration) {
        return obj switch {
            SpellTable t => writer.TrySave(t, iteration),
            VitalTable t => writer.TrySave(t, iteration),
            SkillTable t => writer.TrySave(t, iteration),
            ExperienceTable t => writer.TrySave(t, iteration),
            CharGen t => writer.TrySave(t, iteration),
            GfxObj t => writer.TrySave(t, iteration),
            Setup t => writer.TrySave(t, iteration),
            _ => false
        };
    }

    private bool TrySaveFromBytes(IDatReaderWriter writer, PortalDatEntry entry, int iteration) {
        try {
            return entry.TypeName switch {
                nameof(SpellTable) => UnpackAndSave<SpellTable>(writer, entry.Data, iteration),
                nameof(VitalTable) => UnpackAndSave<VitalTable>(writer, entry.Data, iteration),
                nameof(SkillTable) => UnpackAndSave<SkillTable>(writer, entry.Data, iteration),
                nameof(ExperienceTable) => UnpackAndSave<ExperienceTable>(writer, entry.Data, iteration),
                nameof(CharGen) => UnpackAndSave<CharGen>(writer, entry.Data, iteration),
                nameof(GfxObj) => UnpackAndSave<GfxObj>(writer, entry.Data, iteration),
                nameof(Setup) => UnpackAndSave<Setup>(writer, entry.Data, iteration),
                _ => false
            };
        }
        catch (Exception ex) {
            Log.LogError(ex, "[PortalDatDoc] Failed to unpack-and-save {Type}", entry.TypeName);
            return false;
        }
    }

    private static bool UnpackAndSave<T>(IDatReaderWriter writer, byte[] data, int iteration)
        where T : IDBObj, new() {
        var obj = new T();
        var reader = new DatBinReader(data);
        ((IUnpackable)obj).Unpack(reader);
        return writer.TrySave(obj, iteration);
    }

    public override void Dispose() {
        _objectCache.Clear();
        _unpackFailures.Clear();
        Overlay.Entries.Clear();
    }
}
