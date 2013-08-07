using System;
using System.IO;
using System.Timers;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using System.Web;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;

using PRoCon.Core;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;
using PRoCon.Core.Players;

namespace PRoConEvents
{
    public class pureLogServer : PRoConPluginAPI, IPRoConPluginInterface
    {

        private int pluginEnabled = 0;
        //private String pluginEnabledString = "0";

        private int playerCount;
        private Timer updateTimer;
        private Timer initialTimer;
        private String mySqlHostname;
        private String mySqlPort;
        private String mySqlDatabase;
        private String mySqlUsername;
        private String mySqlPassword;
        private int intervalLength = 60000;

        //private MySqlConnection firstConnection;
        private MySqlConnection confirmedConnection;
        //private bool SqlConnected = true;
        private String bigTableName;
        private String dayTableName;
        private String debugLevelString = "1";
        private int debugLevel = 1;

        private int backupCache = 0;
        private int backupRuns = 0;

        private string heartbeatServerIp;
        private int heartbeatServerPort;
        private string heartbeatClientIdentifier;
        private string sendHeartbeatsString = "0";
        private int sendHeartbeats = 0;

        public pureLogServer()
        {

        }

        public void output(object source, ElapsedEventArgs e)
        {
            if (pluginEnabled > 0)
            {
                //this.toChat("pureLog Server Tracking " + playerCount + " players online.");
                this.toConsole(2, "pureLog Server Tracking " + playerCount + " players online.");
                bool abortUpdate = false;

                //what time is it?
                DateTime rightNow = DateTime.Now;
                String rightNowHour = rightNow.ToString("%H");
                String rightNowMinutes = rightNow.ToString("%m");
                int rightNowMinTotal = (Convert.ToInt32(rightNowHour)) * 60 + Convert.ToInt32(rightNowMinutes);
                int totalPlayerCount = playerCount + backupCache;

                if (backupRuns % 5 == 0)
                {
                    //Check for a new day
                    this.goodMorning();
                    //Insert the latest interval, plus any backup cache
                    
                    MySqlCommand query = new MySqlCommand("INSERT INTO " + dayTableName + " (min, time) VALUES ('" + totalPlayerCount + "','" + rightNowMinTotal + "')", this.confirmedConnection);
                    if (testQueryCon(query))
                    {
                        try { query.ExecuteNonQuery(); }
                        catch (Exception m)
                        {
                            this.toConsole(1, "Couldn't parse query!");
                            this.toConsole(1, m.ToString());
                            abortUpdate = true;
                        }
                    }
                    query.Connection.Close();
                }
                else
                {
                    toConsole(2, "Skipping this day table insertion...");
                    toConsole(2, "Current backup cache value: " + this.backupCache + " // The last " + this.backupRuns + " day table insertions were skipped.");
                    this.backupCache += playerCount;
                    this.backupRuns++;
                }
                
                //Was the insertion a success?
                if (!abortUpdate)
                {
                    toConsole(2, "Added an interval worth " + totalPlayerCount + " for timestamp " + rightNowMinTotal);
                    //Clear out any remaining cache.
                    this.backupRuns = 0;
                    this.backupCache = 0;
                    //Ping!
                    SendHeartbeats();
                }
                else
                {
                    toConsole(1, "There's a connection problem. I'll try again in five minutes and put the next five intervals into the backup cache.");
                    //Add missing minutes to cache.
                    this.backupCache += playerCount;
                    //Consider this run skipped.
                    this.backupRuns++;
                    toConsole(2, "Current backup cache value: " + this.backupCache + " // The last " + this.backupRuns + " day table insertions were skipped.");
                }
            }
        }

        //The first thing the plugin does.
        public void establishFirstConnection(object source, ElapsedEventArgs e)
        {
            bool SqlConnected = true;
            this.toConsole(2, "Trying to connect to " + mySqlHostname + ":" + mySqlPort + " with username " + mySqlUsername);
            MySqlConnection firstConnection = new MySqlConnection("Server=" + mySqlHostname + ";" + "Port=" + mySqlPort + ";" + "Database=" + mySqlDatabase + ";" + "Uid=" + mySqlUsername + ";" + "Pwd=" + mySqlPassword + ";" + "Connection Timeout=5;");
            try { firstConnection.Open(); }
            catch (Exception z)
            {
                this.toConsole(1, "Initial connection error!");
                this.toConsole(1, z.ToString());
                SqlConnected = false;
            }
            //Get ready to rock!
            if (SqlConnected)
            {
                firstConnection.Close();
                this.toConsole(1, "Connection established with " + mySqlHostname + "!");
                this.toConsole(2, "Stopping connection retry attempts timer...");
                this.initialTimer.Stop();
                confirmedConnection = firstConnection;
                this.updateTimer = new Timer();
                this.updateTimer.Elapsed += new ElapsedEventHandler(this.output);
                this.updateTimer.Interval = intervalLength;
                this.updateTimer.Start();
                //this.output();
            }
            else
            {
                this.toConsole(1, "Could not establish an initial connection. I'll try again in a minute.");
                this.toConsole(1, "The restart/restart/restart connection fix should no longer be necessary as of version 1.1.0.");
            }
        }

        //Is it a new day?
        public void goodMorning()
        {
            String dateNow = DateTime.Now.ToString("MMddyyyy");
            String dateYesterday = DateTime.Now.AddDays(-1).ToString("MMddyyyy");

            int rowCount = 999;
            MySqlCommand query = new MySqlCommand("SELECT COUNT(*) FROM " + bigTableName + " WHERE date='" + dateNow + "'", this.confirmedConnection);
            if (testQueryCon(query))
            {
                try { rowCount = int.Parse(query.ExecuteScalar().ToString()); }
                catch (Exception e)
                {
                    this.toConsole(1, "Couldn't parse query!");
                    this.toConsole(1, e.ToString());
                }
            }
            query.Connection.Close();

            //There are no rows with today's date in the big table. Must be a new day!
            if (rowCount == 0)
            {
                bool abortUpdate = false;

                this.toConsole(1, "Today is " + dateNow + ". Good morning!");
                this.toConsole(2, "Summing up yesterday's player minutes...");

                //Adding up yesterday's minutes...
                int minSum = 0;
                query = new MySqlCommand("SELECT SUM(min) FROM " + dayTableName, this.confirmedConnection);
                if (testQueryCon(query))
                {
                    try { minSum = int.Parse(query.ExecuteScalar().ToString()); }
                    catch (Exception e)
                    {
                        this.toConsole(1, "Couldn't parse query! NOTE: If this is the first time running this plugin, or haven't run the plugin in a while, ignore this error. It should go away in the next minute.");
                        this.toConsole(1, e.ToString());
                        abortUpdate = true;
                    }
                } else { abortUpdate = true; }
                query.Connection.Close();

                this.toConsole(2, "Yesterday's sum was " + minSum + " player minutes.");

                if (!abortUpdate)
                {
                    //Update yesterday's minutes...
                    query = new MySqlCommand("UPDATE " + bigTableName + " SET min=" + minSum + " WHERE date='" + dateYesterday + "'", this.confirmedConnection);
                    if (testQueryCon(query))
                    {
                        try { query.ExecuteNonQuery(); }
                        catch (Exception e)
                        {
                            this.toConsole(1, "Couldn't parse query!");
                            this.toConsole(1, e.ToString());
                            abortUpdate = true;
                        }
                    }
                    else { abortUpdate = true; }
                    query.Connection.Close();

                    if (!abortUpdate)
                    {
                        this.toConsole(2, "Updated yesterday's minutes!");
                        //and finally, start a new day
                        query = new MySqlCommand("INSERT INTO " + bigTableName + " (date) VALUES ('" + dateNow + "')", this.confirmedConnection);
                        if (testQueryCon(query))
                        {
                            try { query.ExecuteNonQuery(); }
                            catch (Exception e)
                            {
                                this.toConsole(1, "Couldn't parse query!");
                                this.toConsole(1, e.ToString());
                                abortUpdate = true;
                            }
                        }
                        else { abortUpdate = true; }
                        query.Connection.Close();

                        if (!abortUpdate)
                        {
                            this.toConsole(2, "New big table row inserted!");
                            //clear the day table for a new day

                            query = new MySqlCommand("DELETE FROM " + dayTableName, this.confirmedConnection);
                            if (testQueryCon(query))
                            {
                                try { query.ExecuteNonQuery(); }
                                catch (Exception e)
                                {
                                    this.toConsole(1, "Couldn't parse query!");
                                    this.toConsole(1, e.ToString());
                                    abortUpdate = true;
                                }
                            }
                            else { abortUpdate = true; }
                            query.Connection.Close();
                            if (!abortUpdate)
                            {
                                this.toConsole(2, "Day table reset!");
                                //clear the day table for a new day
                            }
                        }
                    }
                }
            }
            else
            {
                this.toConsole(2, "Same day as usual.");
            }
        }

        public void SendHeartbeats()
        {
            try
            {
                if (this.pluginEnabled > 0 && this.sendHeartbeats > 0)
                {
                    IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(this.heartbeatServerIp),
                                                        this.heartbeatServerPort);
                    Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    byte[] clientId = Encoding.ASCII.GetBytes(this.heartbeatClientIdentifier);

                    try
                    {
                        socket.Connect(serverEndpoint);
                    }
                    catch (Exception exc)
                    {
                        this.toConsole(1, "Heartbeat Exception! " + exc.Message);
                    }
                    try
                    {
                        byte[] numBytes = BitConverter.GetBytes(clientId.Length);
                        socket.Send(numBytes);
                        socket.Send(clientId);
                    }
                    catch (Exception exc)
                    {
                        this.toConsole(1, "Heartbeat Exception! " + exc.Message);
                    }
                    finally
                    {
                        socket.Shutdown(SocketShutdown.Both);
                        socket.Close();
                    }
                }
            }
            catch (SocketException)
            {
            }
        }
    

        //---------------------------------------------------
        //Helper functions
        //---------------------------------------------------

        public void toChat(String message)
        {
            this.ExecuteCommand("procon.protected.send", "admin.say", "pureLogS: " + message, "all");
        }

        public void toConsole(int msgLevel, String message)
        {
            //a message with msgLevel 1 is more important than 2
            if (debugLevel >= msgLevel)
            {
                this.ExecuteCommand("procon.protected.pluginconsole.write", "pureLogS: " + message);
            }
        }

        public bool testQueryCon(MySqlCommand theQuery)
        {
            try { theQuery.Connection.Open(); }
            catch (Exception e)
            {
                this.toConsole(1, "Couldn't open query connection!");
                this.toConsole(1, e.ToString());
                theQuery.Connection.Close();
                return false;
            }
            this.toConsole(2, "Connection OK!");
            return true;
        }

        //---------------------------------------------------
        // Attributes
        //---------------------------------------------------

        public string GetPluginName()
        {
            return "pureLog Server Edition";
        }
        public string GetPluginVersion()
        {
            return "1.2.0";
        }
        public string GetPluginAuthor()
        {
            return "Analytalica";
        }
        public string GetPluginWebsite()
        {
            return "purebattlefield.org";
        }
        public string GetPluginDescription()
        {
            return @"<p>Updates a MySQL database with daily player count logging,
andrecords alog of the total amount of minutes players are
spending in-game on a server per day. In the case of a connection
failure, a local backup cache is created to prevent data loss.</p>
<p>This plugin was developed by analytalica and is currently a
PURE Battlefield exclusive. The heartbeat monitor has <b>not</b>
been tested yet, though as of v1.2.0, it is no longer necessary and
will be removed in the next major revision.</p>
<p><big><b>What's New in 1.2?</b></big></p>
<p>Bugfixes, bugfixes, bugfixes! The plugin is now <b>fully capable</b> of
recovering after a Procon crash or forced layer restart, and <b>no longer needs to be actively
maintained!</b> There will be a minute delay when the plugin
starts to give time for Procon to finish initializing.If an initial
connection can't be established, the plugin will try
again once every minute until the plugin is disabled. Setup
instructions have also been expanded with further details.</p>
<p>And hello Draeger!</p>
<p><big><b>Initial Setup:</b></big><br>
</p>
<ol>
  <li>Create a table in the database (Big Table) with three
columns: id (INT), date (VARCHAR 255), and min (INT). Set id to
auto-increment.</li>
  <li>Make another table in the database (Day Table) with three
columns: id (INT), time (VARCHAR 255), and min (INT). Set id to
auto-increment.</li>
  <li>Fill out ALL plugin settings before starting the plugin.</li>
  <ol>
    <li>Use an IP address for the hostname. </li>
    <li>The default port for remote MySQL
connections is 3306 (on PURE servers, use 3603).</li>
    <li>Set the database you want this plugin to connect to.
Multiple databases will be needed for multiple servers and plugins.</li>
    <li>Provide a username and password combination with the
permissions (SELECT, INSERT, UPDATE, DELETE) necessary to access that
database.</li>
    <li>The debug levels are as follows: 0
suppresses ALL messages (not recommended), 1 shows important messages
only (recommended), and 2 shows ALL messages (useful for step by step
debugging).</li>
    <li>Set the table names to the same names chosen in steps 1
and 2.</li>
  </ol>
  <li>The heartbeat monitor settings can be left blank if the
'Send heartbeats?' option is set to 0.<br>
  </li>
</ol>
<p><b>Steps 1 and 2
can be accomplished using the default MySQL setup commands, which can
be found below.</b> <br>
</p>
<ul>
  <li>CREATE TABLE bigtable(id int NOT NULL AUTO_INCREMENT, date
varchar(255), min int(11), PRIMARY KEY (id));
  </li>
  <li>CREATE TABLE daytable(id int NOT NULL AUTO_INCREMENT, time
varchar(255), min int(11), PRIMARY KEY (id));
  </li>
</ul>
<p>If you choose to run the commands above for the initial setup,
in the plugin settings, set Big Table name to 'bigtable' and Day Table
name to 'daytable'.</p>
<p><big><b>Understanding
the Table Structure:</b></big></p>
<p>Every row in the Big Table stands for a different day, as
indicated by the timestamp found in the date column. The Big Table's
min column stores the total amount of minutes players spent in game
that day. On a 64-player server, there is a maximum of 60*24*64 = 92160
in-game minutes possible per day.</p>
<p>Every row in the Day Table stands for a different interval
(typically polled every minute), as indicated by the timestamp found in
the time column. The Day Table's min column stores the amount of
players recorded during that time interval. At the beginning of each
new day, the total sum of all the intervals is inserted into the Big
Table as an entry for the previous day, and then the Day Table is reset.</p>
<p><big><b>Troubleshooting: </b></big><br>
</p>
<ul>
  <li>The IP address that Procon uses to access the MySQL server
is different from the IP of the layer itself. If a remote connection
can't be established, try using more wildcards in the accepted
connections (do %.%.%.% to test).</li>
</ul>
<p><big><b>Fallbacks: </b></big><br>
</p>
<ul>
  <li>If at any step in the table updating process the connection
fails, the plugin will continue adding minutes to the most recent day.
A new day for the plugin only begins when the connection is successful.
  </li>
  <li>On the case of a MySQL connection failure, the plugin will
skip the next five insertion attempts to avoid overloading PRoCon.
Missing intervals will be summed up into one when a connection is
re-established.</li>
  <li>If an initial connection can't be established, the plugin
will try again once every minute. All error messages will be shown in
the console output with debug level set to 1.</li>
</ul>

                    ";
        }

        //---------------------------------------------------
        // Config (totally not a ripoff of some other plugins)
        //---------------------------------------------------
        public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
        {
            this.RegisterEvents(this.GetType().Name, "OnServerInfo");
            //this.RegisterEvents(this.GetType().Name, "OnPluginLoaded", "OnServerInfo");
            this.ExecuteCommand("procon.protected.pluginconsole.write", "pureLog Server Edition Loaded!");
        }

        public void OnPluginEnable()
        {
            this.pluginEnabled = 1;
            this.toConsole(1, "pureLog Server Edition Running!");
            this.toConsole(2, "The plugin will try and connect once every minute. Please wait...");

            //Delay the first connection by a minute.
            this.initialTimer = new Timer();
            this.initialTimer.Elapsed += new ElapsedEventHandler(this.establishFirstConnection);
            this.initialTimer.Interval = 60000;
            this.initialTimer.Start();

        }

        public void OnPluginDisable()
        {
            this.pluginEnabled = 0;
            this.ExecuteCommand("procon.protected.tasks.remove", "pureLogServer");
            this.toConsole(2, "Stopping connection retry attempts timer...");
            this.initialTimer.Stop();
            this.toConsole(2, "Stopping update timer...");
            this.updateTimer.Stop();
            this.toConsole(1, "pureLog Server Edition Closed.");
        }

        public override void OnServerInfo(CServerInfo csiServerInfo)
        {
            this.playerCount = csiServerInfo.PlayerCount;
        }

        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();
            lstReturn.Add(new CPluginVariable("MySQL Settings|MySQL Hostname", typeof(string), mySqlHostname));
            lstReturn.Add(new CPluginVariable("MySQL Settings|MySQL Port", typeof(string), mySqlPort));
            lstReturn.Add(new CPluginVariable("MySQL Settings|MySQL Database", typeof(string), mySqlDatabase));
            lstReturn.Add(new CPluginVariable("MySQL Settings|MySQL Username", typeof(string), mySqlUsername));
            lstReturn.Add(new CPluginVariable("MySQL Settings|MySQL Password", typeof(string), mySqlPassword));
            lstReturn.Add(new CPluginVariable("Table Names|Big Table", typeof(string), bigTableName));
            lstReturn.Add(new CPluginVariable("Table Names|Day Table", typeof(string), dayTableName));
            lstReturn.Add(new CPluginVariable("Other|Debug Level", typeof(string), debugLevelString));
            lstReturn.Add(new CPluginVariable("Other|Heartbeat Server Port", typeof(string), heartbeatServerPort));
            lstReturn.Add(new CPluginVariable("Other|Heartbeat Server IP", typeof(string), heartbeatServerIp));
            lstReturn.Add(new CPluginVariable("Other|Heartbeat Client Identifier", typeof(string), heartbeatClientIdentifier));
            lstReturn.Add(new CPluginVariable("Other|Send heartbeats?", typeof(string), sendHeartbeatsString));
            return lstReturn;
        }

        public List<CPluginVariable> GetPluginVariables()
        {
            return GetDisplayPluginVariables();
        }

        public void SetPluginVariable(string strVariable, string strValue)
        {
            if (Regex.Match(strVariable, @"MySQL Hostname").Success)
            {
                mySqlHostname = strValue;
            }
            else if (Regex.Match(strVariable, @"MySQL Port").Success)
            {
                int tmp = 3306;
                int.TryParse(strValue, out tmp);
                if (tmp > 0 && tmp < 65536)
                {
                    mySqlPort = strValue;
                }
                else
                {
                    this.ExecuteCommand("procon.protected.pluginconsole.write", "Invalid SQL Port Value.");
                }
            }
            else if (Regex.Match(strVariable, @"MySQL Database").Success)
            {
                mySqlDatabase = strValue;
            }
            else if (Regex.Match(strVariable, @"MySQL Username").Success)
            {
                mySqlUsername = strValue;
            }
            else if (Regex.Match(strVariable, @"MySQL Password").Success)
            {
                mySqlPassword = strValue;
            }
            else if (Regex.Match(strVariable, @"Big Table").Success)
            {
                bigTableName = strValue;
            }
            else if (Regex.Match(strVariable, @"Day Table").Success)
            {
                dayTableName = strValue;
            }
            else if (Regex.Match(strVariable, @"Debug Level").Success)
            {
                debugLevelString = strValue;
                try
                {
                    debugLevel = Int32.Parse(debugLevelString);
                }
                catch (Exception z)
                {
                    toConsole(1, "Invalid debug level! Choose 0, 1, or 2 only.");
                    debugLevel = 1;
                    debugLevelString = "1";
                }
            }
            else if (strVariable == "Heartbeat Server Port")
            {
                Int32.TryParse(strValue, out this.heartbeatServerPort);
            }
            else if (strVariable == "Heartbeat Server IP")
            {
                this.heartbeatServerIp = strValue;
            }
            else if (strVariable == "Heartbeat Client Identifier")
            {
                this.heartbeatClientIdentifier = strValue;
            }
            else if (strVariable == "Send heartbeats?")
            {
                this.sendHeartbeatsString = strValue;
                try
                {
                    sendHeartbeats = Int32.Parse(sendHeartbeatsString);
                }
                catch (Exception z)
                {
                    toConsole(1, "Invalid heartbeat toggle! Choose 0 (false) or 1 (true) only.");
                    sendHeartbeats = 0;
                    sendHeartbeatsString = "0";
                }
            }
        }
    }
}