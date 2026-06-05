using System;
using System.Collections.Generic;
using Serilog;

namespace Photobooth
{
    public enum BoothState
    {
        Idle,        // GreetingPage — kiosk home / admin panel
        StylePick,   // StylePickerPage — AI enhancement style selection
        Shooting,    // ShootPage — EVF + countdown + capture
        Preview,     // ResultsPage — strip preview + print + auto-return countdown
        // Payment   — post-MVP: Nayax terminal gate (Idle → Payment → Shooting)
    }

    public enum FlowTrigger
    {
        StartNormal,     // Normal session start (or post-payment external signal)
        StartAI,         // User chose AI Enhancement flow
        StyleChosen,     // AI style confirmed → proceed to shoot
        StyleCancelled,  // Back from style picker → return to idle
        ShotsDone,       // All photos captured → show results strip
        SessionAborted,  // Back button or camera disconnect during shoot
        PreviewDone,     // Auto-countdown expired or user tapped Start Again
    }

    public sealed class FlowContext
    {
        public List<string>? PhotoPaths { get; init; }
        public int?          SessionId  { get; init; }
    }

    public sealed class FlowController
    {
        private readonly INavigator _navigator;

        public FlowController(INavigator navigator) => _navigator = navigator;

        public BoothState   CurrentState  { get; private set; } = BoothState.Idle;
        public List<string> SessionPhotos { get; private set; } = new();
        public int          SessionId     { get; private set; }

        // AI enhancement flow state — set by StylePickerPage before Shooting
        public bool   AIFlowActive { get; set; }
        public string AIStyleId    { get; set; } = string.Empty;
        public string AIStyleName  { get; set; } = string.Empty;

        public event EventHandler<BoothState>? StateChanged;

        public void Trigger(FlowTrigger trigger, FlowContext? ctx = null)
        {
            var next = Transition(CurrentState, trigger);
            if (next is null)
            {
                Log.Warning("Flow: no transition from {State} on {Trigger} — ignored", CurrentState, trigger);
                return;
            }

            Log.Information("Flow {From} --[{Trigger}]--> {To}", CurrentState, trigger, next.Value);

            if (ctx?.PhotoPaths is not null) SessionPhotos = ctx.PhotoPaths;
            if (ctx?.SessionId  is not null) SessionId     = ctx.SessionId.Value;

            CurrentState = next.Value;
            StateChanged?.Invoke(this, next.Value);
            _navigator.NavigateTo(next.Value);
        }

        private static BoothState? Transition(BoothState state, FlowTrigger trigger) =>
            (state, trigger) switch
            {
                (BoothState.Idle,      FlowTrigger.StartNormal)    => BoothState.Shooting,
                (BoothState.Idle,      FlowTrigger.StartAI)        => BoothState.StylePick,
                (BoothState.StylePick, FlowTrigger.StyleChosen)    => BoothState.Shooting,
                (BoothState.StylePick, FlowTrigger.StyleCancelled) => BoothState.Idle,
                (BoothState.Shooting,  FlowTrigger.ShotsDone)      => BoothState.Preview,
                (BoothState.Shooting,  FlowTrigger.SessionAborted) => BoothState.Idle,
                (BoothState.Preview,   FlowTrigger.PreviewDone)    => BoothState.Idle,
                _ => null,
            };
    }
}
