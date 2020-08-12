using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class Airlock
        {
            private enum PressureState
            {
                Opening,
                Open,
                High,
                LockDown,
                Falling,
                Low,
                LockUp,
                Rising,
                Leak,
                Fault
            };
            private readonly Dictionary<PressureState, String> PressureStateLabel = new Dictionary<PressureState, string> {
                {PressureState.Opening, "Opening both sides"},
                {PressureState.Open, "Open both sides"},
                {PressureState.High, "High pressure"},
                {PressureState.LockDown, "Closing doors" },
                {PressureState.Falling, "Despressurizing"},
                {PressureState.Low, "Vacuum"},
                {PressureState.LockUp, "Closing doors"},
                {PressureState.Rising, "Pressurizing"},
                {PressureState.Leak, "Leak detected"},
                {PressureState.Fault, "Fault detected"}
            };
            public enum RequestState { None, Open, High, Low, Cycle }
            public enum DisplayFormat { None, OneLine, MultiLine, Debug };
            const double AirCloseEnoughThanks = 0.01;

            private List<IMyDoor> insideDoors;
            private List<IMyDoor> outsideDoors;
            private Dictionary<IMyDoor, bool> doorAutomaticallyOpens;
            private bool ventsRemainActiveHigh = false;
            private bool ventsRemainActiveLow = false;
            private List<IMyTextSurface> displays;
            private Dictionary<IMyTextSurface, DisplayFormat> displayFormat;
            private List<IMyLightingBlock> lights;
            private List<IMyAirVent> airVents;
            private List<IMyGasTank> oxygenTanks;
            public String name;
            private PressureState pressureState;
            public RequestState requestState;
            private StringBuilder displayContent;

            public Airlock(string name)
            {
                this.name = name;
                insideDoors = new List<IMyDoor>();
                outsideDoors = new List<IMyDoor>();
                doorAutomaticallyOpens = new Dictionary<IMyDoor, bool>();
                displays = new List<IMyTextSurface>();
                displayFormat = new Dictionary<IMyTextSurface, DisplayFormat>();
                lights = new List<IMyLightingBlock>();
                airVents = new List<IMyAirVent>();
                oxygenTanks = new List<IMyGasTank>();
                pressureState = PressureState.Open;
                requestState = RequestState.None;
            }

            public void setPressureState(String state)
            {
                pressureState = (PressureState)Enum.Parse(typeof(PressureState),state);
            }

            public String getPressureState()
            {
                return pressureState.ToString();
            }

            private PressureState getPressureStateFromRequest()
            {
                switch (requestState)
                {
                    case RequestState.Open:
                        if (pressureState == PressureState.Open)
                            return PressureState.Open;
                        else
                            return PressureState.Opening;
                    case RequestState.High:
                        switch (pressureState)
                        {
                            case PressureState.Falling:
                            case PressureState.Rising:
                                return PressureState.Rising;
                            case PressureState.High:
                                return PressureState.High;
                            default:
                                return PressureState.LockUp;
                        };
                    case RequestState.Low:
                        switch (pressureState)
                        {
                            case PressureState.Falling:
                            case PressureState.Rising:
                                return PressureState.Falling;
                            case PressureState.Low:
                                return PressureState.Low;
                            default:
                                return PressureState.LockDown;
                        };
                    case RequestState.Cycle:
                        switch (pressureState)
                        {
                            case PressureState.Low:
                            case PressureState.LockDown:
                                return PressureState.LockUp;
                            case PressureState.Rising:
                                return PressureState.Falling;
                            case PressureState.Falling:
                                return PressureState.Rising;
                            default:
                                return PressureState.LockDown;
                        }
                    default:
                        return PressureState.Fault; // Not actually reachable
                }
            }

            private PressureState getNextPressureState()
            {
                switch (pressureState)
                {
                    case PressureState.LockDown:
                        return PressureState.Falling;
                    case PressureState.Falling:
                        return PressureState.Low;
                    case PressureState.LockUp:
                        return PressureState.Rising;
                    case PressureState.Rising:
                        return PressureState.High;
                    case PressureState.Opening:
                        return PressureState.Open;
                    default:
                        return pressureState;
                }
            }

            public bool processState()
            {
                if (requestState != RequestState.None)
                {
                    pressureState = getPressureStateFromRequest();
                    requestState = RequestState.None;
                }
                bool stateComplete = true;
                double totalFill;
                switch (pressureState)
                {
                    case PressureState.LockDown:
                    case PressureState.LockUp:
                        foreach (IMyDoor door in outsideDoors)
                        {
                            if (null == door || door.WorldMatrix == MatrixD.Identity || !door.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            if (door.Status == DoorStatus.Closed)
                            {
                                door.Enabled = false;
                            }
                            else
                            {
                                stateComplete = false;
                                door.Enabled = true;
                                door.CloseDoor();
                            }
                        }
                        foreach (IMyDoor door in insideDoors)
                        {
                            if (null == door || door.WorldMatrix == MatrixD.Identity || !door.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            if (door.Status == DoorStatus.Closed)
                            {
                                door.Enabled = false;
                            }
                            else
                            {
                                stateComplete = false;
                                door.Enabled = true;
                                door.CloseDoor();
                            }
                        }
                        break;
                    case PressureState.Falling:
                        if (airVents.Count == 0)
                        {
                            outsideDoors.ForEach(d => { d.Enabled = true; if (doorAutomaticallyOpens[d]) d.OpenDoor(); });
                        }
                        totalFill = 0;
                        foreach (IMyGasTank oxygenTank in oxygenTanks)
                        {
                            if (null == oxygenTank || oxygenTank.WorldMatrix == MatrixD.Identity || !oxygenTank.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            totalFill += oxygenTank.FilledRatio;
                        }
                        if ((totalFill > (double)oxygenTanks.Count - AirCloseEnoughThanks))
                        {
                            foreach (IMyAirVent airVent in airVents)
                            {
                                airVent.Enabled = ventsRemainActiveLow;
                                outsideDoors.ForEach(d => { d.Enabled = true; if (doorAutomaticallyOpens[d]) d.OpenDoor(); });
                            }
                        }
                        else
                        {
                            foreach (IMyAirVent airVent in airVents)
                            {
                                if (null == airVent || airVent.WorldMatrix == MatrixD.Identity || !airVent.IsFunctional)
                                {
                                    pressureState = PressureState.Fault;
                                    return (true);
                                }
                                if ((airVent.Status == VentStatus.Depressurized) || (airVent.Enabled && airVent.GetOxygenLevel() < AirCloseEnoughThanks))
                                {
                                    airVent.Enabled = ventsRemainActiveLow;
                                    outsideDoors.ForEach(d => { d.Enabled = true; if (doorAutomaticallyOpens[d]) d.OpenDoor(); });
                                }
                                else
                                {
                                    airVent.Depressurize = true;
                                    airVent.Enabled = true;
                                    stateComplete = false;
                                }
                                if (!airVent.CanPressurize)
                                {
                                    pressureState = PressureState.Leak;
                                }
                            }
                        }
                        break;
                    case PressureState.Rising:
                        if (airVents.Count == 0)
                        {
                            insideDoors.ForEach(d => { d.Enabled = true; if (doorAutomaticallyOpens[d]) d.OpenDoor(); });
                        }
                        totalFill = 0;
                        foreach (IMyGasTank oxygenTank in oxygenTanks)
                        {
                            if (null == oxygenTank || oxygenTank.WorldMatrix == MatrixD.Identity || !oxygenTank.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            totalFill += oxygenTank.FilledRatio;
                        }
                        if (!(totalFill > AirCloseEnoughThanks))
                        {
                            foreach (IMyAirVent airVent in airVents)
                            {
                                airVent.Enabled = ventsRemainActiveHigh;
                                insideDoors.ForEach(d => { d.Enabled = true; if (doorAutomaticallyOpens[d]) d.OpenDoor(); });
                            }
                        }
                        else
                        {
                            foreach (IMyAirVent airVent in airVents)
                            {
                                if (null == airVent || airVent.WorldMatrix == MatrixD.Identity || !airVent.IsFunctional)
                                {
                                    pressureState = PressureState.Fault;
                                    return (true);
                                }
                                if (airVent.Status == VentStatus.Pressurized)
                                {
                                    airVent.Enabled = ventsRemainActiveHigh;
                                    insideDoors.ForEach(d => { d.Enabled = true; if (doorAutomaticallyOpens[d]) d.OpenDoor(); });
                                }
                                else
                                {
                                    airVent.Depressurize = false;
                                    airVent.Enabled = true;
                                    stateComplete = false;
                                }
                                if (!airVent.CanPressurize)
                                {
                                    pressureState = PressureState.Leak;
                                }
                            }
                        }
                        break;
                    case PressureState.Opening:
                        foreach (IMyAirVent airVent in airVents)
                        {
                            if (null == airVent ||airVent.WorldMatrix == MatrixD.Identity || !airVent.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            airVent.Enabled = ventsRemainActiveLow;
                        }
                        foreach (IMyDoor door in insideDoors)
                        {
                            if (null == door || door.WorldMatrix == MatrixD.Identity || !door.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            door.Enabled = true;
                            if(doorAutomaticallyOpens[door]) door.OpenDoor();
                        }
                        foreach (IMyDoor door in outsideDoors)
                        {
                            if (null == door || door.WorldMatrix == MatrixD.Identity || !door.IsFunctional)
                                {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            door.Enabled = true;
                            if (doorAutomaticallyOpens[door]) door.OpenDoor();
                        }
                        break;
                    default:
                        break;
                }
                if(stateComplete)
                {
                    pressureState = getNextPressureState();
                }
                updateDisplays();
                updateLights();
                return stateComplete;
            }

            public void addDoor(IMyDoor newDoor, bool inside = true, bool automaticallyOpen=true)
            {
                if (inside)
                {
                    insideDoors.Add(newDoor);
                    if (outsideDoors.Contains(newDoor))
                    {
                        outsideDoors.Remove(newDoor);
                    }
                }
                else
                {
                    outsideDoors.Add(newDoor);
                    if (insideDoors.Contains(newDoor))
                    {
                        insideDoors.Remove(newDoor);
                    }
                }
                doorAutomaticallyOpens.Add(newDoor, automaticallyOpen);
            }

            public void addDisplay(IMyTextSurface newDisplay, DisplayFormat newDisplayFormat)
            {
                displays.Add(newDisplay);
                displayFormat.Add(newDisplay, newDisplayFormat);
                newDisplay.PreserveAspectRatio = true;
            }

            public void addLight(IMyLightingBlock newLight)
            {
                lights.Add(newLight);
            }

            public void addAirVent(IMyAirVent newAirVent, bool remainActiveHigh, bool remainActiveLow)
            {
                airVents.Add(newAirVent);
                ventsRemainActiveHigh |= remainActiveHigh;
                ventsRemainActiveLow |= remainActiveLow;
            }

            public void addTank(IMyGasTank newOxygenTank)
            {
                oxygenTanks.Add(newOxygenTank);
            }

            public void updateDisplays()
            {
                // DisplayFormat.Debug output
                foreach (var display in displays)
                {
                    displayContent = new StringBuilder();
                    switch (displayFormat[display])
                    {
                        case DisplayFormat.Debug:
                            displayContent.Append("Airlock information for: "+name+'\n'
                                +PressureStateLabel[this.pressureState]+"\nInterior doors:\n");
                            foreach (var insideDoor in insideDoors) displayContent.Append("  "+insideDoor.CustomName+" ("+(insideDoor.IsFunctional ? insideDoor.Status.ToString() : "Fault")+")\n");
                            displayContent.Append("Exterior doors:\n");
                            foreach (var outsideDoor in outsideDoors) displayContent.Append("  " + outsideDoor.CustomName+" ("+ (outsideDoor.IsFunctional ? outsideDoor.Status.ToString() : "Fault") + ")\n");
                            displayContent.Append("Air vents:\n");
                            foreach (var airVent in airVents) displayContent.Append("  " + airVent.CustomName+" ("+(airVent.IsFunctional ? airVent.Status.ToString() : "Fault" )+")\n");
                            displayContent.Append("Oxygen tanks:\n");
                            foreach (var oxygenTank in oxygenTanks) displayContent.Append($"  {oxygenTank.CustomName} ({oxygenTank.FilledRatio * 100:0.0}%)\n");
                            break;
                        case DisplayFormat.None:
                            break;
                        case DisplayFormat.OneLine:
                            if (showNames)
                                displayContent.Append(this.name+" - ");
                            displayContent.Append(PressureStateLabel[pressureState]+' ');
                            break;
                        case DisplayFormat.MultiLine:
                            if (showNames)
                                displayContent.Append(iniSectionName+": "+name+"\n");
                            displayContent.Append($"{PressureStateLabel[pressureState]}\nInner doors:\n");
                            foreach (IMyDoor door in insideDoors)
                            {
                                displayContent.Append(door.IsFunctional ? door.Status.ToString() + ' ' : "Fault ");
                            }
                            displayContent.Append("\nOuter doors:\n");
                            foreach (IMyDoor door in outsideDoors)
                            {
                                displayContent.Append(door.IsFunctional ? door.Status.ToString() + ' ' : "Fault ");
                            }
                            break;
                    }
                    if (null == display)
                    {
                        pressureState = PressureState.Fault;
                        return;
                    }
                    if (displayFormat[display] != DisplayFormat.None) display.WriteText(displayContent);
                    if (useColours && displayFormat[display] != DisplayFormat.Debug)
                    {
                        switch (pressureState)
                        {
                            case PressureState.Open:
                                display.ClearImagesFromSelection();
                                display.AddImageToSelection("Danger");
                                display.BackgroundColor = Color.DarkGoldenrod;
                                break;
                            case PressureState.High:
                                display.ClearImagesFromSelection();
                                display.BackgroundColor = Color.Green;
                                break;
                            case PressureState.LockDown:
                                display.ClearImagesFromSelection();
                                display.AddImageToSelection("Danger");
                                display.BackgroundColor = Color.Maroon;
                                break;
                            case PressureState.Falling:
                                display.BackgroundColor = Color.DarkCyan;
                                break;
                            case PressureState.Low:
                                display.ClearImagesFromSelection();
                                display.BackgroundColor = Color.Cyan;
                                break;
                            case PressureState.LockUp:
                                display.ClearImagesFromSelection();
                                display.AddImageToSelection("Danger");
                                display.BackgroundColor = Color.Blue;
                                break;
                            case PressureState.Rising:
                                display.BackgroundColor = Color.DarkGreen;
                                break;
                            case PressureState.Leak:
                            case PressureState.Fault:
                                display.ClearImagesFromSelection();
                                display.AddImageToSelection("Danger");
                                display.BackgroundColor = Color.Red;
                                break;
                        }
                    }
                };
            }

            public void updateLights()
            {
                foreach (var light in lights)
                {
                    switch (pressureState)
                    {

                        case PressureState.LockDown:
                        case PressureState.LockUp:
                            if (useColours) light.Color = Color.Maroon;
                            light.Enabled = true;
                            break;
                        case PressureState.Falling:
                            if (useColours) light.Color = Color.DarkOrange;
                            light.Enabled = true;
                            break;
                        case PressureState.Leak:
                        case PressureState.Fault:
                            if (useColours) light.Color = Color.Yellow;
                            light.Enabled = true;
                            break;
                        default:
                            light.Enabled = false;
                            break;
                    }
                }
            }
        }
    }
}

