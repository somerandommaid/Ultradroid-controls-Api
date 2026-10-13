using System;
using UnityEngine;

namespace ULTRADROID.API
{
    /// <summary>A handle to a registered custom button.</summary>
    public sealed class ButtonHandle : IDisposable
    {
        internal readonly ButtonRegistration Registration;

        internal ButtonHandle(ButtonRegistration registration)
        {
            Registration = registration;
            Id = registration.Config.Id;
        }

        public string Id { get; }
        public bool IsReady { get; internal set; }

        /// <summary>The underlying Unity object after creation, or null before creation/after removal.</summary>
        public GameObject GameObject { get; internal set; }

        /// <summary>Removes the custom button. The removal is processed on the Unity update loop.</summary>
        public void Dispose()
        {
            ULTRADROIDControls.Remove(this);
        }
    }
}
