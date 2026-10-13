using System;
using System.Collections.Generic;

namespace ULTRADROID.API
{
    /// <summary>
    /// Public API for adding controls to ULTRAKILL's mobile HUD.
    /// Requires the ULTRADROID.Core plugin to be installed and loaded.
    /// </summary>
    public static class ULTRADROIDControls
    {
        public const string PluginGuid = "somerandommaid.ultradroid.controlsapi";
        public const string PluginVersion = "0.1.0";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ButtonRegistration> Registrations =
            new Dictionary<string, ButtonRegistration>(StringComparer.Ordinal);

        /// <summary>Registers a custom button and returns a handle to it.</summary>
        public static ButtonHandle AddButton(ButtonConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrWhiteSpace(config.Id))
                throw new ArgumentException("ButtonConfig.Id must be a stable, non-empty ID.", nameof(config));

            config.Id = config.Id.Trim();
            if (config.Id.Length > 120)
                throw new ArgumentException("ButtonConfig.Id must be 120 characters or fewer.", nameof(config));

            if (float.IsNaN(config.ScaleMultiplier) || float.IsInfinity(config.ScaleMultiplier) || config.ScaleMultiplier <= 0f)
                throw new ArgumentOutOfRangeException(nameof(config), "ScaleMultiplier must be a finite number greater than zero.");

            if (string.IsNullOrWhiteSpace(config.ControlPath))
                config.ControlPath = "<Keyboard>/f15";

            lock (Gate)
            {
                if (Registrations.TryGetValue(config.Id, out ButtonRegistration existing) && !existing.IsRemoved)
                    throw new InvalidOperationException("A ULTRADROID button with ID '" + config.Id + "' is already registered.");

                var registration = new ButtonRegistration(config);
                Registrations[config.Id] = registration;
                return registration.Handle;
            }
        }

        internal static void Remove(ButtonHandle handle)
        {
            if (handle == null)
                return;

            lock (Gate)
            {
                if (Registrations.TryGetValue(handle.Id, out ButtonRegistration current) &&
                    ReferenceEquals(current, handle.Registration))
                {
                    current.IsRemoved = true;
                }
            }
        }

        internal static ButtonRegistration[] GetRegistrations()
        {
            lock (Gate)
            {
                var result = new ButtonRegistration[Registrations.Count];
                Registrations.Values.CopyTo(result, 0);
                return result;
            }
        }

        internal static void ForgetRemoved(ButtonRegistration registration)
        {
            if (registration == null)
                return;

            lock (Gate)
            {
                if (registration.IsRemoved &&
                    Registrations.TryGetValue(registration.Config.Id, out ButtonRegistration current) &&
                    ReferenceEquals(current, registration))
                {
                    Registrations.Remove(registration.Config.Id);
                }
            }
        }
    }
}
