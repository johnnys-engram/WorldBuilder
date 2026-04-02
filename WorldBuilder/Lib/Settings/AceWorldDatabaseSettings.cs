using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Shared.Lib.Settings;

namespace WorldBuilder.Lib.Settings {
    /// <summary>MySQL connection to an ACE <c>ace_world</c> database (weenie editor, future export hooks).</summary>
    [SettingCategory("ACE World (MySQL)", Order = 1.25)]
    public partial class AceWorldDatabaseSettings : ObservableObject {
        [SettingDescription("MySQL host (e.g. localhost)")]
        [SettingOrder(0)]
        private string _host = "localhost";
        public string Host { get => _host; set => SetProperty(ref _host, value); }

        [SettingDescription("MySQL port")]
        [SettingOrder(1)]
        private int _port = 9000;
        public int Port { get => _port; set => SetProperty(ref _port, value); }

        [SettingDescription("Database name (usually ace_world)")]
        [SettingOrder(2)]
        private string _database = "ace_world";
        public string Database { get => _database; set => SetProperty(ref _database, value); }

        [SettingDescription("MySQL user name")]
        [SettingOrder(3)]
        private string _user = "root";
        public string User { get => _user; set => SetProperty(ref _user, value); }

        [SettingDescription("MySQL password")]
        [SettingOrder(4)]
        private string _password = "";
        public string Password { get => _password; set => SetProperty(ref _password, value); }
    }
}
