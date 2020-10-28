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
        private bool lowImpactMode = false;

        Dictionary<String, Airlock> airlocks = new Dictionary<String, Airlock>();
        readonly MyIni ini = new MyIni();
        MyIniParseResult result;
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
                        String remainActiveInput = ini.Get(iniSectionName, "remainactive").ToString("never").ToLower();
                        bool remainActiveHigh, remainActiveLow;
                        switch(remainActiveInput)
                        {
                            case "always":
                                remainActiveHigh = true;
                                remainActiveLow = true;
                                break;
                            case "high":
                                remainActiveHigh = true;
                                remainActiveLow = false;
                                break;
                            case "low":
                                remainActiveHigh = false;
                                remainActiveLow = true;
                                break;
                            default:
                                remainActiveHigh = false;
                                remainActiveLow = false;
                                break;
                        }
                        airlocks[airlockNameLC].addAirVent((IMyAirVent)airlockBlock,remainActiveHigh,remainActiveLow);
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

        public void populateFromGroups()
        {
            List<IMyTerminalBlock> allLocalBlocks = new List<IMyTerminalBlock>();
            String airlockName;
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(allLocalBlocks, block => block.IsSameConstructAs(Me));
            foreach(IMyTerminalBlock block in allLocalBlocks)
            {
                if (!ini.TryParse(block.CustomData, out result)) continue;
                if (ini.ContainsKey(iniSectionName, "airlock"))
                {
                    block.CustomData = "";
                }
            }
            List<IMyBlockGroup> AirlockGroups = new List<IMyBlockGroup>();
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            IMyBlockGroup ExternalDoorsGroup;
            List<IMyDoor> externalDoors = new List<IMyDoor>();
            GridTerminalSystem.GetBlockGroups(AirlockGroups, g => g.Name.StartsWith(iniSectionName+' '));
            ExternalDoorsGroup = GridTerminalSystem.GetBlockGroupWithName(iniSectionName + "ExternalDoors");
            if (null == ExternalDoorsGroup)
                externalDoors = new List<IMyDoor>();
            else
                ExternalDoorsGroup.GetBlocksOfType<IMyDoor>(externalDoors);
            foreach(IMyBlockGroup group in AirlockGroups)
            {
                airlockName = group.Name.Substring(group.Name.IndexOf(' ')+1);
                group.GetBlocks(blocks);
                foreach (IMyTerminalBlock block in blocks)
                {
                    block.CustomData = "[" + iniSectionName + "]\nAirlock=" + airlockName + (externalDoors.Contains(block) ? "\nside=outer" : "");
                }
            }
            populate();
        }

        public Program()
        {
            if (ini.TryParse(Me.CustomData, out result))
                if (ini.ContainsKey(iniSectionName, "lowimpact"))
                    if (ini.Get(iniSectionName, "lowimpact").ToBoolean())
                        lowImpactMode = true;
            if (lowImpactMode)
                Runtime.UpdateFrequency = UpdateFrequency.None;
            else
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
                    case "destroyandpopulatefromgroups":
                        pbKeyboard.WriteText("Populate from Groups\n", true);
                        populateFromGroups();
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
                    if(!(airlock.processState() || lowImpactMode)) Runtime.UpdateFrequency |= UpdateFrequency.Once;
                }
            }
        }
    }
}

