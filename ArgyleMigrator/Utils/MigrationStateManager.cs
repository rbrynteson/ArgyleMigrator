using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ArgyleMigrator.Models;

namespace ArgyleMigrator.Utils
{
    public class MigrationStateManager
    {
        private static string StateFilePath => Path.Combine(Directory.GetCurrentDirectory(), "migration_state.json");
        private static readonly object FileLock = new object();

        public static MigrationState LoadState()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    lock (FileLock)
                    {
                        var json = File.ReadAllText(StateFilePath);
                        return JsonConvert.DeserializeObject<MigrationState>(json);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading migration state");
            }
            return null;
        }

        public static void SaveState(MigrationState state)
        {
            try
            {
                lock (FileLock)
                {
                    File.WriteAllText(StateFilePath, JsonConvert.SerializeObject(state, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error saving migration state");
            }
        }

        public static void InitializeState(string teamId, string teamName)
        {
            var state = new MigrationState
            {
                TeamId = teamId,
                TeamName = teamName,
                MigrationStarted = DateTime.UtcNow
            };
            SaveState(state);
        }

        public static void UpdateChannelState(string channelId, Action<ChannelMigrationState> updateAction)
        {
            var state = LoadState();
            if (state == null) return;

            var channel = state.Channels.FirstOrDefault(c => c.ChannelId == channelId);
            if (channel == null)
            {
                channel = new ChannelMigrationState { ChannelId = channelId };
                state.Channels.Add(channel);
            }

            updateAction(channel);
            channel.LastUpdated = DateTime.UtcNow;
            SaveState(state);
        }

        public static bool IsChannelMigrated(string channelId)
        {
            var state = LoadState();
            if (state == null) return false;

            var channel = state.Channels.FirstOrDefault(c => c.ChannelId == channelId);
            return channel?.MigrationCompleted ?? false;
        }

        public static void CompleteMigration()
        {
            var state = LoadState();
            if (state == null) return;

            state.MigrationCompleted = DateTime.UtcNow;
            SaveState(state);
        }

        public static void LogChannelError(string channelId, string error)
        {
            UpdateChannelState(channelId, channel =>
            {
                channel.Errors.Add($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {error}");
            });
        }
    }
} 