using Sandbox.Graphics;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using VRageRender;
using VRageRender.Voxels;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        static readonly String iniSectionName = "Airlock";
        private static bool showNames = true;
        private static bool useColours = true;
        static IMyTextSurface pbDisplay,pbKeyboard;
        private String reqCommand, reqAirlock;

        public class Airlock
        {
            private enum PressureState
            {
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

            private List<IMyDoor> insideDoors;
            private List<IMyDoor> outsideDoors;
            private Dictionary<IMyDoor, bool> doorAutomaticallyOpens;
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
                        return PressureState.Open;
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
                        foreach (IMyAirVent airVent in airVents)
                        {
                            if (null == airVent || airVent.WorldMatrix == MatrixD.Identity || !airVent.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            if ((airVent.Status == VentStatus.Depressurized)||(airVent.Enabled && airVent.GetOxygenLevel()<0.01))
                            {
                                airVent.Enabled = false;
                                outsideDoors.ForEach(d => { d.Enabled = true; if(doorAutomaticallyOpens[d]) d.OpenDoor(); });
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
                        foreach(IMyGasTank oxygenTank in oxygenTanks)
                        {
                            if (null == oxygenTank || oxygenTank.WorldMatrix == MatrixD.Identity || !oxygenTank.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            if (oxygenTank.FilledRatio == 0.0) pressureState = PressureState.Low;
                        }
                        break;
                    case PressureState.Rising:
                        foreach (IMyAirVent airVent in airVents)
                        {
                            if (null == airVent || airVent.WorldMatrix == MatrixD.Identity || !airVent.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            if (airVent.Status == VentStatus.Pressurized)
                            {
                                airVent.Enabled = false;
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
                        foreach (IMyGasTank oxygenTank in oxygenTanks)
                        {
                            if (null == oxygenTank || oxygenTank.WorldMatrix == MatrixD.Identity || !oxygenTank.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            if (oxygenTank.FilledRatio == 1.0) pressureState = PressureState.High;
                        }
                        break;
                    case PressureState.Open:
                        foreach (IMyAirVent airVent in airVents)
                        {
                            if (null == airVent ||airVent.WorldMatrix == MatrixD.Identity || !airVent.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            airVent.Enabled = false;
                        }
                        foreach (IMyDoor door in insideDoors)
                        {
                            if (null == door || door.WorldMatrix == MatrixD.Identity || !door.IsFunctional)
                            {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            door.Enabled = true;
                            //door.OpenDoor();
                        }
                        foreach (IMyDoor door in outsideDoors)
                        {
                            if (null == door || door.WorldMatrix == MatrixD.Identity || !door.IsFunctional)
                                {
                                pressureState = PressureState.Fault;
                                return (true);
                            }
                            door.Enabled = true;
                            //door.OpenDoor();
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

            public void addAirVent(IMyAirVent newAirVent)
            {
                airVents.Add(newAirVent);
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
                            foreach (var insideDoor in insideDoors) displayContent.Append("  "+insideDoor.CustomName+" ("+insideDoor.Status+")\n");
                            displayContent.Append("Exterior doors:\n");
                            foreach (var outsideDoor in outsideDoors) displayContent.Append("  " + outsideDoor.CustomName+" ("+outsideDoor.Status+")\n");
                            displayContent.Append("Air vents:\n");
                            foreach (var airVent in airVents) displayContent.Append("  " + airVent.CustomName+" ("+airVent.Status+")\n");
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
                                displayContent.Append(door.Status.ToString() + ' ');
                            }
                            displayContent.Append("\nOuter doors:\n");
                            foreach (IMyDoor door in outsideDoors)
                            {
                                displayContent.Append(door.Status.ToString() + ' ');
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

        Dictionary<String, Airlock> airlocks = new Dictionary<String, Airlock>();
        readonly MyIni ini = new MyIni();
        readonly MyCommandLine commandLine = new MyCommandLine();

        public void populate()
        {
            List<IMyTerminalBlock> allAirlockBlocks = new List<IMyTerminalBlock>();
            String airlockNameLC;
            List<String> airlockNames = new List<string>();
            bool doorSide = true;
            bool doorAutomaticallyOpen = true;
            int dMax = 1;
            IMyTextSurface display;
            MyIniParseResult result;
            Airlock.DisplayFormat displayFormat;

            airlocks.Clear();

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(allAirlockBlocks, f => f.CubeGrid.IsSameConstructAs(Me.CubeGrid) && f.IsFunctional && MyIni.HasSection(f.CustomData, iniSectionName));
            foreach (var airlockBlock in allAirlockBlocks)
            {
                if (!ini.TryParse(airlockBlock.CustomData, out result))
                    continue;
                if (airlockBlock.Equals(Me))
                {
                    showNames = ini.Get(iniSectionName, "shownames").ToBoolean(true);
                    useColours = ini.Get(iniSectionName, "usecolours").ToBoolean(ini.Get(iniSectionName, "usecolors").ToBoolean(true));
                }
                airlockNames.Clear();
                if (ini.ContainsKey(iniSectionName, "airlock"))
                {
                    airlockNames.Add(ini.Get(iniSectionName, "airlock").ToString("airlock"));
                }
                if (ini.ContainsKey(iniSectionName, "airlocks"))
                {
                    airlockNames.AddArray((ini.Get(iniSectionName, "airlocks").ToString().Replace(", ", ",").Split(',')));
                }
                if (airlockNames.Count == 0) { airlockNames.Add("Airlock"); }
                foreach (var airlockName in airlockNames)
                {
                    airlockNameLC = airlockName.ToLower();
                    if (!airlocks.ContainsKey(airlockNameLC))
                    {
                        airlocks.Add(airlockNameLC, new Airlock(airlockName));
                    }
                    if (airlockBlock is IMyDoor)
                    {
                        doorSide = ini.Get(iniSectionName, "side").ToString("in").ToLower().StartsWith("in");
                        doorAutomaticallyOpen = ini.Get(iniSectionName, "auto-open").ToBoolean(true);
                        airlocks[airlockNameLC].addDoor((IMyDoor)airlockBlock, doorSide, doorAutomaticallyOpen);
                    }
                    if (airlockBlock is IMyAirVent)
                    {
                        airlocks[airlockNameLC].addAirVent((IMyAirVent)airlockBlock);
                    }
                    if (airlockBlock is IMyTextSurfaceProvider)
                    {
                        switch(ini.Get(iniSectionName,"format").ToString().ToLower())
                        {
                            case "debug":
                                displayFormat = Airlock.DisplayFormat.Debug;
                                break;
                            case "oneline":
                                displayFormat = Airlock.DisplayFormat.OneLine;
                                break;
                            case "none":
                                displayFormat = Airlock.DisplayFormat.None;
                                break;
                            case "multiline":
                            default:
                                displayFormat = Airlock.DisplayFormat.MultiLine;
                                break;
                        }
                        dMax = ((IMyTextSurfaceProvider)airlockBlock).SurfaceCount;
                        // Read CustomData to find out which surfaces to use, there might be more than one
                        if (dMax > 1)
                        {
                            for (int displayNumber = 0; displayNumber < dMax; ++displayNumber)
                            {
                                if (ini.Get(iniSectionName, $"display{displayNumber}").ToString().ToLower() == airlockNameLC)
                                {
                                    display = ((IMyTextSurfaceProvider)airlockBlock).GetSurface(displayNumber);
                                    display.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                                    airlocks[airlockNameLC].addDisplay(display,displayFormat);
                                }
                            }
                        }
                        else
                        {
                            display = ((IMyTextSurfaceProvider)airlockBlock).GetSurface(0);
                            display.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                            airlocks[airlockNameLC].addDisplay(display,displayFormat);
                        }
                    }
                    if (airlockBlock is IMyGasTank)
                    {
                        // Let's hope the player doesn't want to breathe hydrogen
                        airlocks[airlockNameLC].addTank((IMyGasTank)airlockBlock);
                    }
                    if (airlockBlock is IMyLightingBlock)
                    {
                        // Let's hope the player doesn't want to breathe hydrogen
                        airlocks[airlockNameLC].addLight((IMyLightingBlock)airlockBlock);
                    }
                    airlocks[airlockNameLC].updateDisplays();
                }
            }
            pbDisplay.WriteText("Airlocks configured:\n", false);
            foreach (var airlock in airlocks.Values)
            {
                pbDisplay.WriteText($"Airlock: {airlock.name}\n", true);
            }
            String[] airlockState;
            foreach (var storedState in Storage.Split('\n'))
            {
                airlockState = storedState.Split('\t');
                if (airlocks.ContainsKey(airlockState[0]))
                {
                    airlocks[airlockState[0]].setPressureState(airlockState[1]);
                }
            }
        }

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            pbDisplay = Me.GetSurface(0);
            pbDisplay.Font = "DEBUG";
            pbDisplay.FontSize = 0.8F;
            pbDisplay.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            pbKeyboard = Me.GetSurface(1);
            pbKeyboard.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            pbKeyboard.Font = "DEBUG";
            pbKeyboard.FontSize = 3F;
            populate();
        }

        public void Save()
        {
            StringBuilder airlockStatesToSave = new StringBuilder();
            foreach (var airlock in airlocks.Keys)
            {
                airlockStatesToSave.Append(airlock + '\t' + airlocks[airlock].getPressureState() + '\n');
            }
            Storage = airlockStatesToSave.ToString();
        }

        public void Main(String args)
        {
            if (commandLine.TryParse(args))
            {
                reqCommand = commandLine.Argument(0).ToLower();
                reqAirlock = commandLine.ArgumentCount > 1 ? commandLine.Argument(1).ToLower() : "";
                pbKeyboard.WriteText(args + '\n');
                switch(reqCommand)
                {
                    case "populate":
                        pbKeyboard.WriteText("Populate from Custom Data\n", true);
                        populate();
                        break;
                    case "high":
                        if (airlocks.ContainsKey(reqAirlock))
                        {
                            pbKeyboard.WriteText("Pressurize "+airlocks[reqAirlock].name, true);
                            airlocks[reqAirlock].requestState = Airlock.RequestState.High;
                        }
                        break;
                    case "low":
                        if (airlocks.ContainsKey(reqAirlock))
                        {
                            pbKeyboard.WriteText("Depressurize "+airlocks[reqAirlock].name, true);
                            airlocks[reqAirlock].requestState = Airlock.RequestState.Low;
                        }
                        break;
                    case "cycle":
                        if (airlocks.ContainsKey(reqAirlock))
                        {
                            pbKeyboard.WriteText("Cycle "+airlocks[reqAirlock].name, true);
                            airlocks[reqAirlock].requestState = Airlock.RequestState.Cycle;
                        }
                        break;
                    case "open":
                        if (airlocks.ContainsKey(reqAirlock))
                        {
                            pbKeyboard.WriteText("Force open "+airlocks[reqAirlock].name, true);
                            airlocks[reqAirlock].requestState = Airlock.RequestState.Open;
                        }
                        break;
                    default:
                        pbKeyboard.WriteText("Unknown command", true);
                        break;
                }
            }
            else
            {
                foreach(var airlock in airlocks.Values)
                {
                    if(!airlock.processState()) Runtime.UpdateFrequency |= UpdateFrequency.Once;
                }
            }
        }
    }
}

