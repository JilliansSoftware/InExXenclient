// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Configuration
{
    internal static class ProfileManager
    {
        public static GlobalProfile GlobalProfile { get; private set; }
        public static Profile CurrentProfile { get; private set; }
        public static string ProfilePath { get; private set; }

        private static string _rootPath;
        private static string RootPath
        {
            get
            {
                if (string.IsNullOrEmpty(_rootPath))
                {
                    if (string.IsNullOrWhiteSpace(Settings.GlobalSettings.ProfilesPath))
                    {
                        _rootPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles");
                    }
                    else
                    {
                        _rootPath = Settings.GlobalSettings.ProfilesPath;
                    }
                }

                return _rootPath;
            }
        }

        public static void Load(string servername, string username, string charactername)
        {
            GlobalProfile = ConfigurationResolver.Load<GlobalProfile>(Path.Combine(RootPath, "globalprofile.json"), ProfileJsonContext.DefaultToUse.GlobalProfile) ?? new GlobalProfile();

            string path = FileSystemHelper.CreateFolderIfNotExists(RootPath, username, servername, charactername);
            string fileToLoad = Path.Combine(path, "profile.json");

            ProfilePath = path;
            CurrentProfile = ConfigurationResolver.Load<Profile>(fileToLoad, ProfileJsonContext.DefaultToUse.Profile) ?? NewFromDefault();

            CurrentProfile.Username = username;
            CurrentProfile.ServerName = servername;
            CurrentProfile.CharacterName = charactername;

            ValidateFields(CurrentProfile);

            // IEX: the shard used to turn on "gray out of range" for new characters (a grey fog
            // past the view range); switch it off once for profiles made with that default.
            if (!CurrentProfile.IexOutOfRangeReset)
            {
                CurrentProfile.NoColorObjectsOutOfRange = false;
                CurrentProfile.IexOutOfRangeReset = true;
            }

            IEX.IexDarkUi.Apply(); // IEX: dark windows follow the profile
        }

        public static void SetProfileAsDefault(Profile profile)
        {
            Save(profile, RootPath, "default.json");
        }

        public static Profile NewFromDefault()
        {
            return ConfigurationResolver.Load<Profile>(Path.Combine(RootPath, "default.json"), ProfileJsonContext.DefaultToUse.Profile) ?? IEX.IexDefaults.NewProfile(); // IEX: shard defaults
        }

        private static void ValidateFields(Profile profile)
        {
            if (profile == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(profile.ServerName))
            {
                throw new InvalidDataException();
            }

            if (string.IsNullOrEmpty(profile.Username))
            {
                throw new InvalidDataException();
            }

            if (string.IsNullOrEmpty(profile.CharacterName))
            {
                throw new InvalidDataException();
            }

            if (profile.WindowClientBounds.X < 600)
            {
                profile.WindowClientBounds = new Point(600, profile.WindowClientBounds.Y);
            }

            if (profile.WindowClientBounds.Y < 480)
            {
                profile.WindowClientBounds = new Point(profile.WindowClientBounds.X, 480);
            }
        }

        public static void UnLoadProfile()
        {
            GlobalProfile = null;
            CurrentProfile = null;
            IEX.IexDarkUi.Apply(); // IEX: back to stock art at the login screen
        }

        internal static void Save(Profile profile, string path, string filename = "profile.json")
        {
            ConfigurationResolver.Save(profile, Path.Combine(path, filename), ProfileJsonContext.DefaultToUse.Profile);
            if (GlobalProfile != null)
            {
                ConfigurationResolver.Save(GlobalProfile, Path.Combine(RootPath, "globalprofile.json"), ProfileJsonContext.DefaultToUse.GlobalProfile);
            }
        }
    }
}
