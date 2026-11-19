// The window players see before the game: news, links, the account to play as, other shards, and
// the update (which runs behind it). Windows only, like the updater.
//
// Skipped (straight to the update check and the game) when started with -nolauncher, or when the
// player ticked "Skip this window next time"; holding Shift while starting brings it back.

using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ClassicUO.IEX.Launcher
{
    static class Launcher
    {
        /// <summary>
        /// Shows the launcher and returns the client's command line, or null when this process
        /// should exit (the window was closed, or an update restarted the exe).
        /// </summary>
        public static string[] Run(string[] args)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return args;

            bool skip = args.Any(a => a.Equals("-nolauncher", StringComparison.OrdinalIgnoreCase));
            if (!skip)
            {
                try { skip = LauncherData.SkipWindow && (Control.ModifierKeys & Keys.Shift) == 0; } catch { }
            }

            var clientArgs = args.Where(a => !a.Equals("-nolauncher", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (skip)
                return Updater.Run(clientArgs) ? null : clientArgs;

            Updater.Begin(clientArgs);

            string[] result = null;
            Exception error = null;
            var ui = new Thread(() =>
            {
                try
                {
                    Application.EnableVisualStyles();
                    using var form = new LauncherForm(clientArgs);
                    Application.Run(form);
                    result = form.Result;
                }
                catch (Exception e)
                {
                    error = e;
                }
            }) { Name = "IEX launcher" };
            ui.SetApartmentState(ApartmentState.STA); // WinForms wants STA; the bootstrap's main thread is MTA
            ui.Start();
            ui.Join();

            if (error != null)
            {
                // never let the launcher stand between a player and the game
                Console.WriteLine("launcher failed: " + error);
                while (!Updater.Done)
                    Thread.Sleep(100);
                return Updater.Relaunched ? null : clientArgs;
            }

            return result;
        }
    }
}
