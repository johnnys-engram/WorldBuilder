using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace WorldBuilder.Shared.Lib.AceDb {
    /// <summary>
    /// Thin wrapper around MySqlConnector for ACE ace_world (weenie list + scalar read/write).
    /// </summary>
    public partial class AceDbConnector : IDisposable {
        private readonly AceDbSettings _settings;

        public AceDbConnector(AceDbSettings settings) {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Tests the MySQL connection. Returns null on success or the error message on failure.
        /// </summary>
        public async Task<string?> TestConnectionAsync(CancellationToken ct = default) {
            try {
                await using var conn = new MySqlConnection(_settings.ConnectionString);
                await conn.OpenAsync(ct);
                return null;
            }
            catch (Exception ex) {
                return ex.Message;
            }
        }

        /// <summary>
        /// Weenie name lookup result for pickers (ID, display name, and optional Setup DID for 3D preview).
        /// </summary>
        public record WeenieEntry(uint ClassId, string Name, uint SetupId);

        /// <summary>
        /// Loads weenie class IDs, names, and setup DIDs from ace_world for picker/list UI.
        /// </summary>
        public async Task<List<WeenieEntry>> GetWeenieNamesAsync(string? search = null, int limit = 500, CancellationToken ct = default) {
            var results = new List<WeenieEntry>();
            try {
                await using var conn = new MySqlConnection(_settings.ConnectionString);
                await conn.OpenAsync(ct);

                string sql;
                if (string.IsNullOrWhiteSpace(search)) {
                    sql = @"
                        SELECT n.`object_Id`, n.`value` AS `name`,
                               COALESCE(d.`value`, 0) AS `setup_did`
                        FROM `weenie_properties_string` n
                        LEFT JOIN `weenie_properties_d_i_d` d
                            ON d.`object_Id` = n.`object_Id` AND d.`type` = 1
                        WHERE n.`type` = 1
                        ORDER BY n.`value`
                        LIMIT @limit";
                }
                else {
                    sql = @"
                        SELECT n.`object_Id`, n.`value` AS `name`,
                               COALESCE(d.`value`, 0) AS `setup_did`
                        FROM `weenie_properties_string` n
                        LEFT JOIN `weenie_properties_d_i_d` d
                            ON d.`object_Id` = n.`object_Id` AND d.`type` = 1
                        WHERE n.`type` = 1
                          AND n.`value` LIKE CONCAT('%', @search, '%')
                        ORDER BY n.`value`
                        LIMIT @limit";
                }

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@limit", limit);
                if (!string.IsNullOrWhiteSpace(search))
                    cmd.Parameters.AddWithValue("@search", search.Trim());

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    results.Add(new WeenieEntry(
                        reader.GetUInt32("object_Id"),
                        reader.GetString("name"),
                        reader.IsDBNull(reader.GetOrdinal("setup_did")) ? 0 : reader.GetUInt32("setup_did")
                    ));
                }
            }
            catch (MySqlException) {
            }

            return results;
        }

        /// <summary>
        /// Batch lookup of Setup DIDs (PropertyDataId.Setup = type 1) for a set of weenie class IDs.
        /// </summary>
        public async Task<Dictionary<uint, uint>> GetSetupDidsAsync(
            IEnumerable<uint> weenieClassIds, CancellationToken ct = default) {
            var result = new Dictionary<uint, uint>();
            var idList = new HashSet<uint>(weenieClassIds).ToList();
            if (idList.Count == 0) return result;

            try {
                await using var conn = new MySqlConnection(_settings.ConnectionString);
                await conn.OpenAsync(ct);

                for (int offset = 0; offset < idList.Count; offset += 500) {
                    var batch = idList.Skip(offset).Take(500).ToList();
                    var paramNames = string.Join(",", batch.Select((_, i) => $"@w{offset + i}"));
                    var sql = $@"SELECT `object_Id`, `value`
                                 FROM `weenie_properties_d_i_d`
                                 WHERE `type` = 1 AND `object_Id` IN ({paramNames})";

                    await using var cmd = new MySqlCommand(sql, conn);
                    for (int i = 0; i < batch.Count; i++)
                        cmd.Parameters.AddWithValue($"@w{offset + i}", batch[i]);

                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        result.TryAdd(reader.GetUInt32("object_Id"), reader.GetUInt32("value"));
                }
            }
            catch (MySqlException) {
            }

            return result;
        }

        public void Dispose() { }
    }
}
