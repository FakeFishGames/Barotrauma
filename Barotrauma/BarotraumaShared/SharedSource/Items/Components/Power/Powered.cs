using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
#if CLIENT
using Barotrauma.Sounds;
#endif

namespace Barotrauma.Items.Components
{
    /// <summary>
    /// Order in which power sources will provide to a grid, lower number is higher priority
    /// </summary>
    public enum PowerPriority
    {
        Default = 0, // Use for status effects and/or extraload
        Reactor = 1,
        Relay = 3,
        Battery = 5
    }

    partial class Powered : ItemComponent
    {
       
        private static float updateTimer;
        protected static float UpdateInterval = 1f / 60f;

        /// <summary>
        /// List of all powered ItemComponents
        /// </summary>
        private static readonly List<Powered> poweredList = new List<Powered>();
        public static IEnumerable<Powered> PoweredList
        {
            get { return poweredList; }
        }

        public static List<Connection> ChangedConnections = new List<Connection>();

        public static Dictionary<string, GridInfo> Grids
        {
            get => grids;
        }

        protected static Dictionary<string, GridInfo> grids = new Dictionary<string, GridInfo>();


        /// <summary>
        /// The amount of power currently consumed by the item. Negative values mean that the item is providing power to connected items
        /// </summary>
        protected float currPowerConsumption;

        /// <summary>
        /// Current voltage of the item (load / power)
        /// </summary>
        private float voltage;

        /// <summary>
        /// The minimum voltage required for the item to work
        /// </summary>
        private float minVoltage;

        /// <summary>
        /// The maximum amount of power the item can draw from connected items
        /// </summary>
        protected float powerConsumption;

        protected Connection powerIn, powerOut;

        [Editable, Serialize(0.5f, true, description: "The minimum voltage required for the device to function. " +
            "The voltage is calculated as power / powerconsumption, meaning that a device " +
            "with a power consumption of 1000 kW would need at least 500 kW of power to work if the minimum voltage is set to 0.5.")]
        public float MinVoltage
        {
            get { return powerConsumption <= 0.0f ? 0.0f : minVoltage; }
            set { minVoltage = value; }
        }

        [Editable, Serialize(0.0f, true, description: "How much power the device draws (or attempts to draw) from the electrical grid when active.")]
        public float PowerConsumption
        {
            get { return powerConsumption; }
            set { powerConsumption = value; }
        }
        
        [Serialize(false, true, description: "Is the device currently active. Inactive devices don't consume power.")]
        public override bool IsActive
        {
            get { return base.IsActive; }
            set
            {
                base.IsActive = value;
                if (!value)
                {
                    currPowerConsumption = 0.0f;
                }
            }
        }

        [Serialize(0.0f, true, description: "The current power consumption of the device. Intended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
        public float CurrPowerConsumption
        {
            get {return currPowerConsumption; }
            set { currPowerConsumption = value; }
        }

        [Serialize(0.0f, true, description: "The current voltage of the item (calculated as power consumption / available power). Intended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
        public float Voltage
        {
            get { 
                if (powerIn != null )
                {
                    if (powerIn.Grid != null)
                    {
                        return powerIn.Grid.Voltage;
                    }
                }
                else if (powerOut != null){
                    if (powerOut.Grid != null)
                    {
                        return powerOut.Grid.Voltage;
                    }
                }
                return voltage;
            }
            set {
                if (powerIn != null)
                {
                    if (powerIn.Grid != null)
                    {
                        powerIn.Grid.Voltage = Math.Max(0.0f, value);
                    }
                }
                else if (powerOut != null)
                {
                    if (powerOut.Grid != null)
                    {
                        powerOut.Grid.Voltage = Math.Max(0.0f, value);
                    }
                }
                voltage = Math.Max(0.0f, value);
            }
        }

        [Editable, Serialize(true, true, description: "Can the item be damaged by electomagnetic pulses.")]
        public bool VulnerableToEMP
        {
            get;
            set;
        }

        public Powered(Item item, XElement element)
            : base(item, element)
        {
            poweredList.Add(this);
            InitProjectSpecific(element);
        }

        partial void InitProjectSpecific(XElement element);

        protected void UpdateOnActiveEffects(float deltaTime)
        {
            if (currPowerConsumption <= 0.0f)
            {
                //if the item consumes no power, ignore the voltage requirement and
                //apply OnActive statuseffects as long as this component is active
                if (PowerConsumption <= 0.0f)
                {
                    ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
                }
                return;
            }

            if (Voltage > minVoltage)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }
#if CLIENT
            if (Voltage > minVoltage)
            {
                if (!powerOnSoundPlayed && powerOnSound != null)
                {
                    SoundPlayer.PlaySound(powerOnSound.Sound, item.WorldPosition, powerOnSound.Volume, powerOnSound.Range, hullGuess: item.CurrentHull, ignoreMuffling: powerOnSound.IgnoreMuffling);                    
                    powerOnSoundPlayed = true;
                }
            }
            else if (Voltage < 0.1f)
            {
                powerOnSoundPlayed = false;
            }
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateOnActiveEffects(deltaTime);
        }

        public override void OnItemLoaded()
        {
            if (item.Connections == null) { return; }
            foreach (Connection c in item.Connections)
            {
                if (!c.IsPower) { continue; }
                if (this is PowerTransfer pt)
                {
                    if (c.Name == "power_in")
                    {
                        powerIn = c;
                    }
                    else if (c.Name == "power_out")
                    {
                        powerOut = c;
                        if (this is Reactor)
                        {
                            powerOut.priority = (int)PowerPriority.Reactor;
                        }
                        else if (this is PowerContainer)
                        {
                            powerOut.priority = (int)PowerPriority.Battery;
                        }
                        else if (this is RelayComponent)
                        {
                            powerOut.priority = (int)PowerPriority.Relay;
                        }
                    }
                    else if (c.Name == "power")
                    {
                        powerIn = powerOut = c;
                    }
                }
                else
                {
                    if (c.IsOutput)
                    {
                        if (c.Name == "power_in")
                        {
#if DEBUG
                            DebugConsole.ThrowError($"Item \"{item.Name}\" has a power output connection called power_in. If the item is supposed to receive power through the connection, change it to an input connection.");
#else
                            DebugConsole.NewMessage($"Item \"{item.Name}\" has a power output connection called power_in. If the item is supposed to receive power through the connection, change it to an input connection.", Color.Orange);
#endif
                        }
                        powerOut = c;
                        if (this is Reactor)
                        {
                            powerOut.priority = (int)PowerPriority.Reactor;
                        }
                        else if (this is PowerContainer)
                        {
                            powerOut.priority = (int)PowerPriority.Battery;
                        }
                        else if (this is RelayComponent)
                        {
                            powerOut.priority = (int)PowerPriority.Relay;
                        }
                    }
                    else
                    {
                        if (c.Name == "power_out")
                        {
#if DEBUG
                            DebugConsole.ThrowError($"Item \"{item.Name}\" has a power input connection called power_out. If the item is supposed to output power through the connection, change it to an output connection.");
#else
                            DebugConsole.NewMessage($"Item \"{item.Name}\" has a power input connection called power_out. If the item is supposed to output power through the connection, change it to an output connection.", Color.Orange);
#endif
                        }
                        powerIn = c;
                    }
                }
            }
        }
        

        /// <summary>
        /// Allocate electrical devices into their grids based on connections
        /// 
        /// </summary>
        /// <param name="UseCache">Use previous grids and change in connections</param>
        public static void UpdateGrids(bool UseCache = true)
        {

            //Don't use cache if there is no existing grids
            if (Grids.Count > 0 && UseCache)
            {
                // Delete all grids that were affected
                foreach (Connection c in ChangedConnections)
                {
                    if (c.Grid != null)
                    {
                        if (Grids.ContainsKey(c.Grid.ID))
                        {
                            Grids.Remove(c.Grid.ID);
                        }
                        c.Grid = null;
                    }
                }

                foreach (Connection c in ChangedConnections)
                {
                    //Make sure the connection grid hasn't been resolved by another connection update
                    //Ensure the connection has other connections
                    if (c.Grid == null && c.Recipients.Count > 0 && c.Item.Condition > 0.0f)
                    {
                        GridInfo grid = propagateGrid(c);
                        Grids[grid.ID] = grid;
                    }
                }
            }
            else
            {
                // Clear all grid IDs from connections
                foreach (Powered powered in poweredList)
                {
                    //Only check devices with connectors
                    if (powered.powerIn != null)
                    {
                        powered.powerIn.Grid = null;
                    }
                    if (powered.powerOut != null)
                    {
                        powered.powerOut.Grid = null;
                    }
                }

                Grids.Clear();

                foreach (Powered powered in poweredList)
                {
                    //Probe through all connections that don't have a gridID
                    if (powered.powerIn != null && powered.powerIn.Grid == null && powered.powerIn != powered.powerOut && powered.Item.Condition > 0.0f)
                    {
                        // Only create grids for networks with more than 1 device
                        if (powered.powerIn.Recipients.Count > 0)
                        {
                            GridInfo grid = propagateGrid(powered.powerIn);
                            Grids[grid.ID] = grid;
                        }
                    }

                    if (powered.powerOut != null && powered.powerOut.Grid == null && powered.Item.Condition > 0.0f)
                    {
                        // Only create grids for networks with more than 1 device
                        if (powered.powerOut.Recipients.Count > 0)
                        {
                            GridInfo grid = propagateGrid(powered.powerOut);
                            Grids[grid.ID] = grid;
                        }
                    }
                }
            }

            //Clear changed connections after each update
            ChangedConnections.Clear();
        }

        private static GridInfo propagateGrid(Connection conn)
        {
            // Generate unique Key
            string ID = RandomString(4);
            while (Powered.Grids.ContainsKey(ID))
            {
                ID = RandomString(4);
            }

            return propagateGrid(conn, ID);
        }

        private static GridInfo propagateGrid(Connection conn, string gridID)
        {
            Stack<Connection> probeStack = new Stack<Connection>();

            GridInfo grid = new GridInfo(gridID);

            probeStack.Push(conn);

            // Non recursive approach to traversing connection tree
            while (probeStack.Count > 0)
            {
                Connection c = probeStack.Pop();
                c.Grid = grid;
                grid.AddConnection(c);

                //Add on recipients 
                foreach (Connection otherC in c.Recipients)
                {
                    // Only add valid connections
                    if (otherC.Grid != grid && (otherC.Grid == null || !Grids.ContainsKey(otherC.Grid.ID)) && otherC.IsPower)
                    {
                        if (otherC.Item.Condition <= 0.0f)
                        {
                            continue;
                        }

                        otherC.Grid = grid; //Assigning ID early prevents unncessary adding to stack
                        probeStack.Push(otherC);
                    }
                }
            }

            return grid;
        }

        //Generate random ID names for grids
        private static string RandomString(int length)
        {
            string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            char[] stringChars = new char[length];
            Random random = new Random();

            for (int i = 0; i < stringChars.Length; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            return new String(stringChars);
        }

        /// <summary>
        /// Update the power calculations of all devices and grids
        /// Updates grids in the order of
        /// ConnCurrConsumption - Get load of device/ flag it as an outputting connection
        /// -- If outputting power --
        /// MinMaxPower - Minimum and Maximum power output of the connection for devices to coordinate
        /// ConnPowerOut - Final power output based on the sum of the MinMaxPower
        /// -- Finally --
        /// GridResolved - Indicate that a connection's grid has been finished being calculated
        /// 
        /// Power outputting devices are calculated in stages based on their priority
        /// Reactors will output first, followed by relays then batteries.
        /// 
        /// </summary>
        /// <param name="deltaTime"></param>
        public static void UpdatePower(float deltaTime)
        {
            //Don't update the power if the round is ending
            if (GameMain.GameSession.RoundEnding)
            {
                return;
            }

            //Only update the power at the given update interval
            /*
            //Not use currently as update interval of 1/60
            if (updateTimer > 0.0f)
            {
                updateTimer -= deltaTime;
                return;
            }
            updateTimer = UpdateInterval;
            */

#if CLIENT
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            //Ensure all grids are updated correctly and have the correct connections
            UpdateGrids();

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("GridUpdate", sw.ElapsedTicks);
            sw.Restart();
#endif

            //Reset all grids
            foreach (KeyValuePair<string, GridInfo> grid in Grids)
            {
                //Wipe priority groups as connections can change to not be outputting -- Can be improved caching wise --
                grid.Value.SrcGroups.Clear();
                grid.Value.Power = 0;
                grid.Value.Load = 0;
            }

            //Determine if devices are adding a load or providing power, also resolve solo nodes
            foreach (Powered powered in poweredList)
            {
                // Handle the device if its got a power connection
                if (powered.powerIn != null && powered.powerOut != powered.powerIn)
                {
                    //Get the new load for the connection
                    float currLoad = powered.ConnCurrConsumption(powered.powerIn);

                    //If its a load update its grid load
                    if (currLoad >= 0)
                    {
                        powered.CurrPowerConsumption = currLoad;
                        if (powered.powerIn.Grid != null)
                        {
                            powered.powerIn.Grid.Load += currLoad;
                        }
                    }
                    else if (powered.powerIn.Grid != null)
                    {
                        //If connected to a grid add as a source to be processed
                        powered.powerIn.Grid.AddSrc(powered.powerIn);
                    }
                    else
                    {
                        //Perform power calculations for the singular connection
                        powered.CurrPowerConsumption = powered.ConnPowerOut(powered.powerIn, 0, powered.MinMaxPowerOut(powered.powerIn, 0), 0);
                        powered.GridResolved(powered.powerIn);
                    }
                }

                //Handle the device power depending on if its powerout
                if (powered.powerOut != null)
                {
                    //Get the connection's load
                    float currLoad = powered.ConnCurrConsumption(powered.powerOut);

                    //Update the device's output load to the correct variable
                    if (powered is PowerTransfer pt)
                    {
                        pt.PowerLoad = currLoad;
                    }
                    else if (powered is PowerContainer pc)
                    {
                        // PowerContainer handle its own output value
                    }
                    else
                    {
                        powered.CurrPowerConsumption = currLoad;
                    }

                    if (currLoad >= 0)
                    {
                        //Add to the grid load if possible
                        if (powered.powerOut.Grid != null)
                        {
                            powered.powerOut.Grid.Load += currLoad;
                        }
                    }
                    else if (powered.powerOut.Grid != null)
                    {
                        //Add connection as a source to be processed
                        powered.powerOut.Grid.AddSrc(powered.powerOut);
                    }
                    else
                    {
                        //Perform power calculations for the singular connection
                        float loadOut = powered.ConnPowerOut(powered.powerOut, 0, powered.MinMaxPowerOut(powered.powerOut, 0), 0);
                        if (powered is PowerTransfer pt2)
                        {
                            pt2.PowerLoad = loadOut;
                        }
                        else if (powered is PowerContainer pc)
                        {
                            // PowerContainer handle its own output value
                        }
                        else
                        {
                            powered.CurrPowerConsumption = loadOut;
                        }

                        //Indicate grid is resolved as it was the only device
                        powered.GridResolved(powered.powerOut);
                    }
                }
            }

            //Iterate through all grids to determine the power on the grid
            foreach (KeyValuePair<string, GridInfo> gridKvp in Grids)
            {
                //Iterate through the priority src groups lowest first
                foreach (KeyValuePair<int, SrcGroup> priorityKvp in gridKvp.Value.SrcGroups)
                {
                    priorityKvp.Value.MinMaxPower = Vector3.Zero;

                    //Iterate through all connections in the group to get their minmax power and sum them
                    foreach (Connection c in priorityKvp.Value.Connections)
                    {
                        Powered device = c.Item.GetComponent<Powered>();
                        priorityKvp.Value.MinMaxPower -= device.MinMaxPowerOut(c, gridKvp.Value.Load);
                    }

                    //Iterate through all connections to get their final power out provided the min max information
                    float addedPower = 0;
                    foreach (Connection c in priorityKvp.Value.Connections)
                    {
                        Powered device = c.Item.GetComponent<Powered>();
                        addedPower -= device.ConnPowerOut(c, gridKvp.Value.Power, priorityKvp.Value.MinMaxPower, gridKvp.Value.Load);
                    }

                    //Add the power to the grid
                    gridKvp.Value.Power += addedPower;
                }

                //Calcualte Grid voltage, limit between 0 - 1000
                float newVoltage = MathHelper.Min(gridKvp.Value.Power / MathHelper.Max(gridKvp.Value.Load, 0.1f), 1000);
                if (float.IsNegative(newVoltage))
                {
                    newVoltage = 0.0f;
                }

                gridKvp.Value.Voltage = newVoltage;

                //Iterate through all connections on that grid and run their gridResolved function
                foreach (Connection con in gridKvp.Value.Connections)
                {
                    Powered device = con.Item.GetComponent<Powered>();
                    device.GridResolved(con);
                }
            }

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("PowerUpdate", sw.ElapsedTicks);
#endif

        }

        /// <summary>
        /// Current load consumption of the device or negative to flag the connection as providing power
        /// </summary>
        /// <param name="conn">Connection to calculate load for</param>
        /// <returns></returns>
        public virtual float ConnCurrConsumption(Connection conn = null)
        {
            // If a handheld device there is no consumption
            if (powerIn == null && powerOut == null)
            {
                return 0;
            }

            // Add extraload for PowerTransfer devices
            if (this is PowerTransfer pt)
            {
                return PowerConsumption + pt.ExtraLoad;
            }
            else if (conn != this.powerIn || !IsActive)
            {
                //If not the power in connection or is inactive there is no draw
                return 0;
            }

            //Otherwise return the max powerconsumption of the device
            return PowerConsumption;
        }

        /// <summary>
        /// Minimum and Maximum power the connection can provide, negative values indicte power can be provided
        /// </summary>
        /// <param name="conn">Connection being queried about its power capabilities</param>
        /// <param name="load">Load of the connected grid</param>
        /// <returns>Vector3 with X as Minimum power out and Y as Maximum power out, the Z is an optional variable to help be used in calculations </returns>
        /// 
        public virtual Vector3 MinMaxPowerOut(Connection conn, float load = 0)
        {
            Vector3 MinMaxPower = new Vector3();

            // If powerin connection return the CurrPowerConsumption 
            if (conn == powerIn)
            {
                MinMaxPower.X = CurrPowerConsumption;
                MinMaxPower.Y = CurrPowerConsumption;
            }
            else if (this is PowerTransfer pt)
            {
                //If its a powerTransfer device use PowerLoad
                MinMaxPower.X = pt.PowerLoad;
                MinMaxPower.Y = pt.PowerLoad;
            }

            return MinMaxPower;
        }

        /// <summary>
        /// Finalize how much power the device will be outputting to the connection
        /// </summary>
        /// <param name="conn">Connection being queried</param>
        /// <param name="power">Current grid power</param>
        /// <param name="minMaxPower">Vector3 containing minimum grid power(X), Max grid power devices can output(Y) and extra variable to help in calculations(Z)</param>
        /// <param name="load">Current load on the grid</param>
        /// <returns>Power pushed to the grid (Negative means adding for consistency)</returns>
        public virtual float ConnPowerOut(Connection conn, float power, Vector3 minMaxPower, float load)
        {
            return conn == powerIn ? MathHelper.Min(CurrPowerConsumption, 0) : 0;
        }

        //Perform updates for the device after the connected grid has resolved its power calculations i.e. storing voltage for later ticks
        public virtual void GridResolved(Connection conn) { }

        /// <summary>
        /// Returns the amount of power that can be supplied by batteries directly connected to the item
        /// </summary>
        protected float GetAvailableBatteryPower(bool outputOnly = true)
        {

            float availablePower = 0.0f;
            
            //Iterate through all containers to get total charge
            foreach(PowerContainer pc in GetConnectedBatteries(outputOnly))
            {
                float batteryPower = Math.Min(pc.Charge * 3600.0f, pc.MaxOutPut);
                availablePower += batteryPower;
            }

            return availablePower;
        }

        /// <summary>
        /// Efficient method to retrieve the batteries connected to the device
        /// </summary>
        /// <returns>All connected powercontainers</returns>
        protected List<PowerContainer> GetConnectedBatteries(bool outputOnly = true)
        {
            List<PowerContainer> batteries = new List<PowerContainer>();
            GridInfo supplyingGrid = null;

            //Determine supplying grid, prefer PowerIn connection 
            if (powerIn != null)
            {
                if (powerIn.Grid != null)
                {
                    supplyingGrid = powerIn.Grid;
                }
            }
            else if (powerOut != null)
            {
                if (powerOut.Grid != null)
                {
                    supplyingGrid = powerOut.Grid;
                }
            }

            if (supplyingGrid != null)
            {
                //Iterate through all connections to fine powerContainers
                foreach (Connection c in supplyingGrid.Connections)
                {
                    PowerContainer pc = c.Item.GetComponent<PowerContainer>();
                    if (pc != null && (!outputOnly || pc.powerOut == c))
                    {
                        batteries.Add(pc);
                    }
                }
            }

            return batteries;
        }

        protected override void RemoveComponentSpecific()
        {
            //Flag power connections to be updated
            if (item.Connections != null)
            {
                foreach (Connection c in item.Connections)
                {
                    if (c.IsPower && c.Grid != null)
                    {
                        ChangedConnections.Add(c);
                    }
                }
            }

            base.RemoveComponentSpecific();
            poweredList.Remove(this);
        }
    }

    partial class GridInfo
    {
        // Custom nickname for a powergrid, derived from any device on the grid that provides a nickname
        public string ID = "";
        public float Voltage = 0;
        public float Load = 0;
        public float Power = 0;

        public List<Connection> Connections = new List<Connection>();
        public SortedList<int, SrcGroup> SrcGroups = new SortedList<int, SrcGroup>();

        public GridInfo(string iD)
        {
            ID = iD;
        }

        public void RemoveConnection(Connection c)
        {
            Connections.Remove(c);

            //Remove the grid if it has no devices
            if (Connections.Count == 0 && Powered.Grids.ContainsKey(ID))
            {
                Powered.Grids.Remove(ID);
            }
        }

        public void AddConnection(Connection c)
        {
            Connections.Add(c);
        }

        public void AddSrc(Connection c)
        {
            if (this.SrcGroups.ContainsKey(c.priority))
            {
                this.SrcGroups[c.priority].Connections.Add(c);
            }
            else
            {
                SrcGroup group = new SrcGroup();
                group.Priority = c.priority;
                group.Connections.Add(c);
                this.SrcGroups[c.priority] = group;
            }
        }
    }

    partial class SrcGroup
    {
        public Vector3 MinMaxPower = new Vector3();
        public int Priority = 0;
        public List<Connection> Connections = new List<Connection>();
    }
}
