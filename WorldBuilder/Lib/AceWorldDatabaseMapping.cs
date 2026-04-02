using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Lib.AceDb;

namespace WorldBuilder.Lib {
    public static class AceWorldDatabaseMapping {
        public static AceDbSettings ToAceDbSettings(this AceWorldDatabaseSettings s) => new() {
            Host = s.Host,
            Port = s.Port,
            Database = s.Database,
            User = s.User,
            Password = s.Password,
        };
    }
}
