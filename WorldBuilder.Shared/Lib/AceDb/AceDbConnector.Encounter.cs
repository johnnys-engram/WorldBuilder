using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace WorldBuilder.Shared.Lib.AceDb {
    public partial class AceDbConnector {
        public record EncounterRow(
            int Landblock,
            uint WeenieClassId,
            int CellX,
            int CellY,
            string WeenieName);

        /// <summary>
        /// One-shot query: for a set of landblock IDs returns encounters joined all the way through
        /// generator -> spawned mob -> Setup DID. This is the batch path used for landscape rendering.
        /// </summary>
        public async Task<List<EncounterSpawn>> GetEncounterSpawnsForAreaAsync(
            IEnumerable<int> landblockIds, CancellationToken ct = default) {
            var ids = landblockIds.Distinct().ToList();
            if (ids.Count == 0) return [];

            var results = new List<EncounterSpawn>();
            try {
                await using var conn = new MySqlConnection(_settings.ConnectionString);
                await conn.OpenAsync(ct);

                // Two-hop query: encounter -> generator weenie (level 1) -> if that is also a
                // generator (weenie.type=1) follow one more hop to get the actual creature's Setup DID.
                var paramNames = string.Join(",", ids.Select((_, i) => $"@lb{i}"));
                var sql = $@"
                    SELECT e.`landblock`, e.`cell_X`, e.`cell_Y`,
                           e.`weenie_Class_Id`                                                AS gen_id,
                           COALESCE(g2.`weenie_Class_Id`, g.`weenie_Class_Id`)                AS spawn_id,
                           COALESCE(sname2.`value`, sname.`value`, '?')                       AS spawn_name,
                           COALESCE(sdid2.`value`,  sdid.`value`,  0)                         AS setup_did,
                           COALESCE(fscale2.`value`, fscale.`value`, 1.0)                     AS spawn_scale
                    FROM `encounter` e
                    -- Level 1: encounter landblock generator -> its first spawn row
                    LEFT JOIN (
                        SELECT `object_Id`, MIN(`id`) AS first_id
                        FROM   `weenie_properties_generator`
                        WHERE  `object_Id` IN (SELECT DISTINCT `weenie_Class_Id` FROM `encounter` WHERE `landblock` IN ({paramNames}))
                        GROUP  BY `object_Id`
                    ) gmin ON gmin.`object_Id` = e.`weenie_Class_Id`
                    LEFT JOIN `weenie_properties_generator` g  ON g.`id`  = gmin.`first_id`
                    -- Check if level-1 spawn is itself a generator (weenie.type = 1 = Generic)
                    LEFT JOIN `weenie` w1 ON w1.`class_Id` = g.`weenie_Class_Id`
                    LEFT JOIN `weenie_properties_string` sname
                           ON sname.`object_Id` = g.`weenie_Class_Id` AND sname.`type` = 1
                    LEFT JOIN `weenie_properties_d_i_d` sdid
                           ON sdid.`object_Id`  = g.`weenie_Class_Id` AND sdid.`type`  = 1
                    LEFT JOIN `weenie_properties_float`  fscale
                           ON fscale.`object_Id` = g.`weenie_Class_Id` AND fscale.`type` = 54
                    -- Level 2: only when level-1 spawn is a generator, follow it one more hop
                    LEFT JOIN (
                        SELECT `object_Id`, MIN(`id`) AS first_id
                        FROM   `weenie_properties_generator`
                        GROUP  BY `object_Id`
                    ) gmin2 ON gmin2.`object_Id` = g.`weenie_Class_Id` AND w1.`type` = 1
                    LEFT JOIN `weenie_properties_generator` g2  ON g2.`id`  = gmin2.`first_id`
                    LEFT JOIN `weenie_properties_string` sname2
                           ON sname2.`object_Id` = g2.`weenie_Class_Id` AND sname2.`type` = 1
                    LEFT JOIN `weenie_properties_d_i_d` sdid2
                           ON sdid2.`object_Id`  = g2.`weenie_Class_Id` AND sdid2.`type`  = 1
                    LEFT JOIN `weenie_properties_float`  fscale2
                           ON fscale2.`object_Id` = g2.`weenie_Class_Id` AND fscale2.`type` = 54
                    WHERE  e.`landblock` IN ({paramNames})";

                await using var cmd = new MySqlCommand(sql, conn);
                for (int i = 0; i < ids.Count; i++)
                    cmd.Parameters.AddWithValue($"@lb{i}", ids[i]);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    var setupDid = reader.IsDBNull(reader.GetOrdinal("setup_did"))
                        ? 0u : reader.GetUInt32("setup_did");
                    if (setupDid == 0) continue; // no model, skip

                    var spawnId = reader.IsDBNull(reader.GetOrdinal("spawn_id"))
                        ? reader.GetUInt32("gen_id") : reader.GetUInt32("spawn_id");

                    var scale = reader.IsDBNull(reader.GetOrdinal("spawn_scale"))
                        ? 1.0f : reader.GetFloat("spawn_scale");
                    if (scale <= 0f) scale = 1.0f;

                    results.Add(new EncounterSpawn(
                        Landblock: reader.GetInt32("landblock"),
                        CellX:     reader.GetInt32("cell_X"),
                        CellY:     reader.GetInt32("cell_Y"),
                        SpawnId:   spawnId,
                        SpawnName: reader.GetString("spawn_name"),
                        SetupDid:  setupDid,
                        Scale:     scale));
                }
            }
            catch (MySqlException ex) {
                throw new InvalidOperationException(
                    $"Failed to query encounter area: {ex.Message}", ex);
            }
            return results;
        }

        public record EncounterSpawn(
            int    Landblock,
            int    CellX,
            int    CellY,
            uint   SpawnId,
            string SpawnName,
            uint   SetupDid,
            float  Scale = 1.0f);

        /// <summary>
        /// Returns encounter rows joined with weenie names from the configured database.
        /// </summary>
        public async Task<List<EncounterRow>> GetEncountersAsync(
            int limit = 5, CancellationToken ct = default) {
            var results = new List<EncounterRow>();
            try {
                await using var conn = new MySqlConnection(_settings.ConnectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    SELECT e.`landblock`, e.`weenie_Class_Id`, e.`cell_X`, e.`cell_Y`,
                           COALESCE(s.`value`, '?') AS `weenie_name`
                    FROM   `encounter` e
                    LEFT JOIN `weenie_properties_string` s
                           ON  s.`object_Id` = e.`weenie_Class_Id` AND s.`type` = 1
                    LIMIT  @limit";

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@limit", limit);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    results.Add(new EncounterRow(
                        Landblock:    reader.GetInt32("landblock"),
                        WeenieClassId: reader.GetUInt32("weenie_Class_Id"),
                        CellX:        reader.GetInt32("cell_X"),
                        CellY:        reader.GetInt32("cell_Y"),
                        WeenieName:   reader.GetString("weenie_name")));
                }
            }
            catch (MySqlException ex) {
                throw new InvalidOperationException(
                    $"Failed to query encounter table: {ex.Message}", ex);
            }

            return results;
        }

        public record DIdRow(ushort Type, uint Value);

        /// <summary>
        /// Returns all weenie_properties_d_i_d rows for a given object_Id.
        /// </summary>
        public async Task<List<DIdRow>> GetWeenieDidsAsync(
            uint objectId, int limit = 20, CancellationToken ct = default) {
            var results = new List<DIdRow>();
            try {
                await using var conn = new MySqlConnection(_settings.ConnectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    SELECT `type`, `value`
                    FROM   `weenie_properties_d_i_d`
                    WHERE  `object_Id` = @objectId
                    ORDER  BY `type`
                    LIMIT  @limit";

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@objectId", objectId);
                cmd.Parameters.AddWithValue("@limit", limit);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    results.Add(new DIdRow((ushort)reader.GetInt32("type"), reader.GetUInt32("value")));
            }
            catch (MySqlException ex) {
                throw new InvalidOperationException(
                    $"Failed to query weenie_properties_d_i_d: {ex.Message}", ex);
            }
            return results;
        }

        /// <summary>
        /// Returns the Setup DID (type=1) for a weenie, or 0 if not found.
        /// </summary>
        public async Task<uint> GetWeenieSetupDidAsync(
            uint objectId, CancellationToken ct = default) {
            try {
                await using var conn = new MySqlConnection(_settings.ConnectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    SELECT `value`
                    FROM   `weenie_properties_d_i_d`
                    WHERE  `object_Id` = @objectId AND `type` = 1
                    LIMIT  1";

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@objectId", objectId);

                var result = await cmd.ExecuteScalarAsync(ct);
                return result == null || result == DBNull.Value ? 0u : Convert.ToUInt32(result);
            }
            catch (MySqlException) {
                return 0;
            }
        }

        public record GeneratorRow(
            float Probability,
            uint SpawnWeenieClassId,
            string SpawnWeenieName,
            float Delay,
            int InitCreate,
            int MaxCreate,
            int WhenCreate);

        /// <summary>
        /// Returns weenie_properties_generator rows for a given object_Id, joined with
        /// weenie_properties_string so each row includes the spawned weenie's name.
        /// </summary>
        public async Task<List<GeneratorRow>> GetWeenieGeneratorsAsync(
            uint objectId, int limit = 2, CancellationToken ct = default) {
            var results = new List<GeneratorRow>();
            try {
                await using var conn = new MySqlConnection(_settings.ConnectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    SELECT g.`probability`, g.`weenie_Class_Id`,
                           COALESCE(s.`value`, '?') AS `spawn_name`,
                           COALESCE(g.`delay`, 0)   AS `delay`,
                           g.`init_Create`, g.`max_Create`, g.`when_Create`
                    FROM   `weenie_properties_generator` g
                    LEFT JOIN `weenie_properties_string` s
                           ON  s.`object_Id` = g.`weenie_Class_Id` AND s.`type` = 1
                    WHERE  g.`object_Id` = @objectId
                    LIMIT  @limit";

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@objectId", objectId);
                cmd.Parameters.AddWithValue("@limit", limit);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    results.Add(new GeneratorRow(
                        Probability:       reader.GetFloat("probability"),
                        SpawnWeenieClassId: reader.GetUInt32("weenie_Class_Id"),
                        SpawnWeenieName:   reader.GetString("spawn_name"),
                        Delay:             reader.GetFloat("delay"),
                        InitCreate:        reader.GetInt32("init_Create"),
                        MaxCreate:         reader.GetInt32("max_Create"),
                        WhenCreate:        reader.GetInt32("when_Create")));
                }
            }
            catch (MySqlException ex) {
                throw new InvalidOperationException(
                    $"Failed to query weenie_properties_generator: {ex.Message}", ex);
            }

            return results;
        }
    }
}
