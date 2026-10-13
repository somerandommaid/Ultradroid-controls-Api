namespace ULTRADROID.API
{
    internal sealed class ButtonRegistration
    {
        internal ButtonRegistration(ButtonConfig config)
        {
            Config = config;
            Handle = new ButtonHandle(this);
        }

        internal ButtonConfig Config { get; }
        internal ButtonHandle Handle { get; }
        internal bool IsRemoved { get; set; }
    }
}
