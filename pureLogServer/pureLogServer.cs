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
        private int playerCount;
        private Timer updateTimer;
        private Timer initialTimer;
        private String mySqlHostname = "";
        private String mySqlPort = "";
        private String mySqlDatabase = "";
        private String mySqlUsername = "";
        private String mySqlPassword = "";

        //private MySqlConnection firstConnection;
        private MySqlConnection confirmedConnection;
        private bool hasConfirmedConnection = false;
        //private bool SqlConnected = true;
        private String bigTableName = "bigtable";
        private String dayTableName = "daytable";

        private int debugLevel = 1;
        private int backupCache = 0;
        private int backupRuns = 0;

        private String pl2_mySqlHostname = "";
        private String pl2_mySqlPort = "";
        private String pl2_mySqlUsername = "";
        private String pl2_mySqlPassword = "";
        private String pl2_mySqlDatabase = "";
        private String pl2_ippTableName = "ipptable";
        private String pl2_eventKey = "";
        private MySqlConnection pl2_confirmedConnection;
        private bool pl2_hasConfirmedConnection = false;
        //Dictionary: <GUID, playerName>
        private Dictionary<String, String> pl2_inGameGUIDs = new Dictionary<String, String>();

        public pureLogServer()
        {
        }

		//Primary operations. output() is run once every minute.
        public void output(object source, ElapsedEventArgs e)
        {
            if (pluginEnabled > 0)
            {
                if (this.hasConfirmedConnection)
                {
                    if (this.pl2_hasConfirmedConnection)
                        this.toConsole(2, "Both logging operations are enabled.");
                    else
                        this.toConsole(2, "Only player minute logging operations are enabled.");
                }
                else
                {
                    this.toConsole(2, "Only individual player playtime logging operations are enabled.");
                }

                if (this.hasConfirmedConnection)
                {
                    this.toConsole(2, "pureLog Server Tracking " + playerCount + " players online.");
                    this.toConsole(3, "Calling list players.");
                    this.ExecuteCommand("procon.protected.send", "admin.listPlayers", "all");

                    bool abortUpdate = false;

                    //what time is it?
                    DateTime rightNow = DateTime.Now;
                    String rightNowHour = rightNow.ToString("%H");
                    String rightNowMinutes = rightNow.ToString("%m");
                    //Okay in retrospect this was a really dumb conversion workaround, but if it ain't broke, don't fix it.
                    //This gets the time in minutes since 00:00.
                    int rightNowMinTotal = (Convert.ToInt32(rightNowHour)) * 60 + Convert.ToInt32(rightNowMinutes);
                    int totalPlayerCount = playerCount + backupCache;

                    if (backupRuns % 5 == 0)
                    {
                        //Check for a new day
                        this.goodMorning();
                        //Insert the latest interval, plus any backup cache
                        //The 'min' and 'time' columns take the 'totalPlayerCount' and 'rightNowMinTotal' values respectively.
                        //Use the connection established when the plugin was started.
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

                if (pl2_hasConfirmedConnection)
                {
                    //pureLog 2
                    //Get the last monday.
                    DateTime lastMonday = DateTime.Now;
                    while(lastMonday.DayOfWeek != DayOfWeek.Monday)
                        lastMonday = lastMonday.AddDays(-1);
                    this.toConsole(2, "Last Monday: " + lastMonday.ToString("yyyy-MM-dd"));

                    if (pl2_inGameGUIDs.Count > 0)
                    {
                        StringBuilder userTimeQuery = new StringBuilder();
                        foreach (KeyValuePair<String, String> pair in this.pl2_inGameGUIDs)
                        {
                            userTimeQuery.Append("INSERT INTO " + this.pl2_ippTableName + " (name, guid, weekOf, time, eventKey) VALUES('" + pair.Value + "', '" + pair.Key + "', '" + lastMonday.ToString("yyyy-MM-dd") + "', 1, '" + this.pl2_eventKey + "') ON DUPLICATE KEY UPDATE name=VALUES(name), time = time + 1; ");
                        }
                        this.toConsole(3, userTimeQuery.ToString());

                        MySqlCommand query = new MySqlCommand(userTimeQuery.ToString(), this.pl2_confirmedConnection);
                        if (testQueryCon(query))
                        {
                            try { query.ExecuteNonQuery(); }
                            catch (Exception x)
                            {
                                this.toConsole(1, "Couldn't parse query!");
                                this.toConsole(1, x.ToString());
                            }
                        }
                        query.Connection.Close();
                    }
                    //string testTimeQuery = "INSERT INTO " + this.pl2_ippTableName + " (id, name, age) VALUES(1, 'A', 19) ON DUPLICATE KEY UPDATE name=VALUES(name), age = age + 1; ";
                }
            }
        }

        //Is it a new day? 
		//The goodMorning() function is meant to handle ALL maintenance tasks that recur with every new day.
		//It works by checking to see if a row in the bigTable exists for the current day.
        public void goodMorning()
        {
            //Get the date string values for today and yesterday.
            String dateNow = DateTime.Now.ToString("MMddyyyy");
            String dateYesterday = DateTime.Now.AddDays(-1).ToString("MMddyyyy");

            int rowCount = 999;
            //Does a row containing today's date value exist?
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

            if (rowCount == 0)
            {
				//There are no rows with today's date in the big table. Must be a new day!
				//Insert all tasks that run every new day here.
				
				//pureLog player-minute counter reset
                bool abortUpdate = false;

                this.toConsole(1, "Today is " + dateNow + ". Good morning!");
                this.toConsole(2, "Summing up yesterday's player minutes...");
				//Update yesterday with the content from today.
                //Calls updateBig.
                if (!updateBig(dateNow, dateYesterday))
                {
                    this.toConsole(2, "Updated yesterday's minutes!");
                    //and finally, start a new day
					//Insert a new row in the big table for today,
                    //and clear the day table.
                    query = new MySqlCommand("INSERT INTO " + bigTableName + " (date) VALUES ('" + dateNow + "'); " + "DELETE FROM " + dayTableName + "; " + "ALTER TABLE " + dayTableName + " AUTO_INCREMENT = 1;", this.confirmedConnection);
                    toConsole(3, "Executing Query: INSERT INTO " + bigTableName + " (date) VALUES ('" + dateNow + "'); " + "DELETE FROM " + dayTableName + "; " + "ALTER TABLE " + dayTableName + " AUTO_INCREMENT = 1;");
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
                }
            }
            else
            {
                toConsole(2, "pureLog 1.5 thinks it's the same day.");
            }
        }

        public Boolean updateBig(string dateNow, string dateYesterday)
        {
            bool abortUpdate = false;

            //this.toConsole(1, "Today is " + dateNow + ". Good morning!");
            this.toConsole(2, "pureLog 1.5 New Update Function...");
            //Update yesterday's minutes...
            //Note the nested MySQL function. The min in bigTable is set to the total sum of min in the dayTable. Clever, huh?
            //emptyTime is the amount of time the server is found empty, aka the number of rows where the player count (min) is 0.
            MySqlCommand query = new MySqlCommand("UPDATE " + bigTableName + " SET min=(SELECT SUM(min) FROM " + dayTableName + "), emptyTime=(SELECT COUNT(*) FROM " + dayTableName + " WHERE min=0) WHERE date='" + dateYesterday + "'", this.confirmedConnection);
            toConsole(3, "Executing Query: " + "UPDATE " + bigTableName + " SET min=(SELECT SUM(min) FROM " + dayTableName + "), emptyTime=(SELECT COUNT(*) FROM " + dayTableName + " WHERE min=0) WHERE date='" + dateYesterday + "'");
            if (testQueryCon(query))
            {
                try { query.ExecuteNonQuery(); }
                catch (Exception e)
                {
                    this.toConsole(1, "Couldn't parse query! pureLog 1.5+ Update Function.");
                    this.toConsole(1, e.ToString());
                    abortUpdate = true;
                }
            }
            else { abortUpdate = true; }
            query.Connection.Close();
            this.toConsole(2, "pureLog 1.5+ Update Complete!");

            return abortUpdate;
        }

        public void toConsole(int msgLevel, String message)
        {
            //a message with msgLevel 1 is more important than 2
            if (debugLevel >= msgLevel)
            {
                this.ExecuteCommand("procon.protected.pluginconsole.write", "pureLog2.0: " + message);
            }
        }
		
		//Test the connection to see if it's valid.
        //Run this every time, just to be safe. 
        //IF CONNECTION OK, MAKE SURE TO CLOSE THE CONNECTION AFTERWARDS IN YOUR OTHER CODE!
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

        public string GetPluginName()
        {
            return "pureLog Server Edition";
        }
        public string GetPluginVersion()
        {
            return "2.0.0";
        }
        #region Description
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
            return @"<p>pureLog is a MySQL database driven game-time analytics plugin
for PRoCon. Its primary functions are to measure daily server
popularity
by logging the total amount of time spent in-game by
players, designated as player-minutes, and to measure the game time of
individual players on a week to week basis.<br>
</p>
<p>This plugin was developed by Analytalica and is currently a
PURE Battlefield exclusive.</p>
<p><big><b>Initial Setup:</b></big><br>
</p>
These features are configured individually. To disable either feature,
simply leave all the configuration fields blank. You may get an error
message, but the plugin will continue operations with only one feature
connected.<br>
<br>
Configuring Total Player-Minutes Tracking:<br>
<ol>
  <li>Make a new MySQL database, or choose an existing one.</li>
  <li>With the database selected, run the first two MySQL
commands included in the .sql file. This generates the bigtable and
daytable.</li>
  <li>Use an IP address for the hostname, or localhost. </li>
  <li>Set the right port value. The default port for remote MySQL
connections is 3306.</li>
  <li>Set the database name you want this plugin to connect to,
as chosen in steps 1 and 2.</li>
  <li>Provide a username and password combination with the
permissions&nbsp;SELECT, INSERT, UPDATE, DELETE, ALTER that can
access the database chosen in steps 1 and 2.</li>
  <li>The debug levels are as follows: 0
suppresses ALL messages (not recommended), 1 shows important messages
only (recommended), and 2 shows important debug messages.</li>
  <li>If the table names were changed in step 2, reconfigure them
accordingly. The default values are bigtable and daytable.</li>
</ol>
Configuring Individual Player Playtime Tracking:<br>
<ol>
  <li>Make a new MySQL database, or choose an existing one. It is
fine to use the same one for both features.</li>
  <li>With the database selected, run the third MySQL command
included in the .sql file. This generates the ipptable.</li>
  <li>Use an IP address for the hostname, or localhost. </li>
  <li>Set the right port value. The default port for remote MySQL
connections is 3306.</li>
  <li>Set the database name you want this plugin to connect to,
as chosen in steps 1 and 2.</li>
  <li>Provide a username and password combination with the
permissions&nbsp;SELECT, INSERT, UPDATE, DELETE, ALTER that can
access the database chosen in steps 1 and 2.</li>
  <li>The debug levels are as follows: 0
suppresses ALL messages (not recommended), 1 shows important messages
only (recommended), and 2 shows important debug messages.</li>
  <li>If the table name was changed in step 2, reconfigure them
accordingly. The default value is ipptable.</li>
  <li>Set a unique server/event key for this server. During
special events, you can reconfigure the key to separate data collected
during events from the normal data.</li>
</ol>
<ol>
  <ol>
  </ol>
</ol>
<p><big><b>How it Works:</b></big></p>
<p>Every row in the Big Table stands for a different day, as
indicated by the timestamp found in the date column. The Big Table's
min column stores the total amount of minutes players spent in game
that day. On a 64-player server, there is a maximum of 60*24*64 = 92160
in-game minutes possible per day. The emptyTime column records the
amount of minutes the server is empty at zero players.</p>
<p>Every row in the Day Table stands for a different interval
polled every minute, as indicated by the timestamp found in
the time column. The Day Table's min column stores the amount of
players recorded during that time interval. At the beginning of each
new day, the total sum of all the intervals is inserted into the Big
Table as an entry for the previous day, and then the Day Table is reset.</p>
<p>Each minute, players found online are credited an additional
minute in the 'Individual Player Playtime' ipptable. The table adds a
new row or increments based on the timestamp and server/event key. Each
new week starts on Monday.</p>
<p><big><b>Troubleshooting: </b></big><br>
</p>
<ul>
  <li>The IP address that Procon uses to access the MySQL
server
may be different from the IP of the layer itself. If a remote
connection
can't be established, try using more wildcards in the accepted
connections (do %.%.%.% to test).</li>
  <li>If the incorrect port value is used but all other
credentials are correct, you may not see any error messages. Manually
query the database to make sure the plugin is tracking properly.</li>
</ul>
<p><big><b>Fallbacks: </b></big><br>
</p>
<ul>
  <li>If at any step in the table updating process the
connection
fails, the plugin will continue adding minutes to the most recent day.
A new day for the plugin only begins when the connection is successful.
  </li>
  <li>On the case of a MySQL connection failure, the plugin
will
skip the next five insertion attempts to avoid overloading PRoCon and
the server.
Missing intervals will be summed up into one when a connection is
re-established.</li>
  <li>If an initial connection can't be established, the plugin
will try again once every minute. All error messages will be shown in
the console output with debug level set to 1.</li>
</ul>

";
        }
        #endregion

        public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
        {
            //this.RegisterEvents(this.GetType().Name, "OnServerInfo", "OnListPlayers");
            this.RegisterEvents(this.GetType().Name, "OnPluginLoaded", "OnServerInfo", "OnListPlayers");
            this.ExecuteCommand("procon.protected.pluginconsole.write", "pureLog Server Edition Loaded!");
        }

        public void OnPluginEnable()
        {
            this.pluginEnabled = 1;
            this.toConsole(1, "pureLog Server Edition Running!");
            this.toConsole(2, "The plugin will try and connect once every minute. Please wait...");
            this.pl2_inGameGUIDs = new Dictionary<String, String>();

            //pureLog 2.0: Set an update timer, but try establishing the first connection immediately.
            this.initialTimer = new Timer();
            this.initialTimer.Elapsed += new ElapsedEventHandler(this.establishFirstConnection);
			//Run the function "establishFirstConnection" in two seconds.
            this.initialTimer.Interval = 60000;
            this.initialTimer.Start();
            this.pl2_eFC();
        }
		
        //The first thing the plugin does.
        public void establishFirstConnection(object source, ElapsedEventArgs e)
        {
            this.pl2_eFC();
        }

        public void pl2_eFC()
        {
            //Connect to player minutes server.
            this.hasConfirmedConnection = true;
            this.toConsole(2, "Trying to connect to " + mySqlHostname + ":" + mySqlPort + " with username " + mySqlUsername);
            MySqlConnection firstConnection = new MySqlConnection("Server=" + mySqlHostname + ";" + "Port=" + mySqlPort + ";" + "Database=" + mySqlDatabase + ";" + "Uid=" + mySqlUsername + ";" + "Pwd=" + mySqlPassword + ";" + "Connection Timeout=5;");
            try { firstConnection.Open(); }
            catch (Exception z)
            {
                this.toConsole(1, "Player Minutes MySQL DB Initial connection error!");
                this.toConsole(1, z.ToString());
                this.hasConfirmedConnection = false;
            }
            firstConnection.Close();

            //Connect to individual player playtime server.
            this.pl2_hasConfirmedConnection = true;
            this.toConsole(2, "Trying to connect to " + pl2_mySqlHostname + ":" + pl2_mySqlPort + " with username " + pl2_mySqlUsername);
            MySqlConnection secondConnection = new MySqlConnection("Server=" + pl2_mySqlHostname + ";" + "Port=" + pl2_mySqlPort + ";" + "Database=" + pl2_mySqlDatabase + ";" + "Uid=" + pl2_mySqlUsername + ";" + "Pwd=" + pl2_mySqlPassword + ";" + "Connection Timeout=5;");
            try { secondConnection.Open(); }
            catch (Exception z)
            {
                this.toConsole(1, "Individual Player Playtime MySQL DB Initial connection error!");
                this.toConsole(1, z.ToString());
                this.pl2_hasConfirmedConnection = false;
            }
            secondConnection.Close();

            bool ready = false;
            //Get ready to rock!
            if (hasConfirmedConnection && !pl2_hasConfirmedConnection)
            {
                this.toConsole(1, "Connection established with " + mySqlHostname + " (player minutes DB)!");
                this.toConsole(1, "I was unable to connect to " + pl2_mySqlHostname + " (individual player playtime DB). Individual player playtime tracking features have been disabled.");
                ready = true;
            }
            else if (!hasConfirmedConnection && pl2_hasConfirmedConnection)
            {
                this.toConsole(1, "Connection established with " + pl2_mySqlHostname + " (individual player playtime DB)!");
                this.toConsole(1, "I was unable to connect to " + pl2_mySqlHostname + " (player minutes DB). Total player minute tracking features have been disabled..");
                ready = true;
            }
            else if (hasConfirmedConnection && pl2_hasConfirmedConnection)
            {
                this.toConsole(1, "Connection established with both " + mySqlHostname + " (player minutes DB) and " + pl2_mySqlHostname + " (individual player playtime DB)!");
                ready = true;
            }
            else if (!hasConfirmedConnection && !pl2_hasConfirmedConnection)
            {
                this.toConsole(1, "Could not establish an initial connection with either database. I'll try again in a minute.");
                ready = false;
            }

            if (ready)
            {
                if (String.IsNullOrEmpty(this.pl2_eventKey) && pl2_hasConfirmedConnection)
                    this.toConsole(1, "Warning: The server/event key is blank! It is strongly recommended you give each server/event a specific key for identification.");

                //Stop the timer that attempts connections.
                this.initialTimer.Stop();
                this.confirmedConnection = firstConnection;
                this.pl2_confirmedConnection = secondConnection;
                this.updateTimer = new Timer();
                this.updateTimer.Elapsed += new ElapsedEventHandler(this.output);
                this.updateTimer.Interval = 60000;
                this.updateTimer.Start();
                this.toConsole(1, "Starting operations.");
                //this.output();
            }
        }

        public void OnPluginDisable()
        {
            this.pluginEnabled = 0;
            this.pl2_inGameGUIDs = new Dictionary<String, String>();
			//Does this actually do anything? I dunno.
            this.ExecuteCommand("procon.protected.tasks.remove", "pureLogServer");
            this.toConsole(2, "Stopping connection retry attempts timer...");
            this.initialTimer.Stop();
            this.toConsole(2, "Stopping update timer...");
            this.updateTimer.Stop();
            this.hasConfirmedConnection = false;
            this.pl2_hasConfirmedConnection = false;
            try
            {
                this.confirmedConnection.Close();
                this.pl2_confirmedConnection.Close();
            }
            catch (Exception e)
            {
                this.toConsole(1, e.ToString());
            }
            this.toConsole(1, "pureLog Server Edition Closed.");
        }

        public override void OnServerInfo(CServerInfo csiServerInfo)
        {
            this.playerCount = csiServerInfo.PlayerCount;
        }

        public override void OnListPlayers(List<CPlayerInfo> players, CPlayerSubset subset)
        {
            if (pluginEnabled > 0)
            {
                this.toConsole(3, "OnListPlayers was called.");
                Dictionary<String, String> pl2_newGUIDs = new Dictionary<String, String>();
                foreach (CPlayerInfo player in players)
                {
                    pl2_newGUIDs.Add(player.GUID, player.SoldierName);
                }
                this.pl2_inGameGUIDs = pl2_newGUIDs;
                this.toConsole(3, "Printing GUID pairs (if any).");
                foreach (KeyValuePair<String, String> pair in this.pl2_inGameGUIDs)
                {
                    this.toConsole(3, pair.Key + " , " + pair.Value);
                }
            }
        }

        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();
            //MySQL connection info.
            lstReturn.Add(new CPluginVariable("1) Total Player-Minutes Tracking|MySQL Hostname", typeof(string), mySqlHostname));
            lstReturn.Add(new CPluginVariable("1) Total Player-Minutes Tracking|MySQL Port", typeof(string), mySqlPort));
            lstReturn.Add(new CPluginVariable("1) Total Player-Minutes Tracking|MySQL Database", typeof(string), mySqlDatabase));
            lstReturn.Add(new CPluginVariable("1) Total Player-Minutes Tracking|MySQL Username", typeof(string), mySqlUsername));
            lstReturn.Add(new CPluginVariable("1) Total Player-Minutes Tracking|MySQL Password", typeof(string), mySqlPassword));
            //Table info.
            lstReturn.Add(new CPluginVariable("1) Total Player-Minutes Tracking|Big Table Name", typeof(string), bigTableName));
            lstReturn.Add(new CPluginVariable("1) Total Player-Minutes Tracking|Day Table Name", typeof(string), dayTableName));

            //pureLog 2
            lstReturn.Add(new CPluginVariable("2) Individual Player Playtime Tracking|ipp MySQL Hostname", typeof(string), pl2_mySqlHostname));
            lstReturn.Add(new CPluginVariable("2) Individual Player Playtime Tracking|ipp MySQL Port", typeof(string), pl2_mySqlPort));
            lstReturn.Add(new CPluginVariable("2) Individual Player Playtime Tracking|ipp MySQL Database", typeof(string), pl2_mySqlDatabase));
            lstReturn.Add(new CPluginVariable("2) Individual Player Playtime Tracking|ipp MySQL Username", typeof(string), pl2_mySqlUsername));
            lstReturn.Add(new CPluginVariable("2) Individual Player Playtime Tracking|ipp MySQL Password", typeof(string), pl2_mySqlPassword));
            //Table info.
            lstReturn.Add(new CPluginVariable("2) Individual Player Playtime Tracking|ipp Table Name", typeof(string), pl2_ippTableName));
            lstReturn.Add(new CPluginVariable("2) Individual Player Playtime Tracking|ipp Server/Event Key", typeof(string), pl2_eventKey));

            lstReturn.Add(new CPluginVariable("3) Other|Debug Level", typeof(string), debugLevel.ToString()));
            return lstReturn;
        }

        public List<CPluginVariable> GetPluginVariables()
        {
            return GetDisplayPluginVariables();
        }

        public void SetPluginVariable(string strVariable, string strValue)
        {
            if (strVariable.Contains("MySQL Hostname") && !strVariable.Contains("ipp"))
            {
                mySqlHostname = strValue;
            }
            else if (strVariable.Contains("MySQL Port") && !strVariable.Contains("ipp"))
            {
                int tmp = 3306;
                int.TryParse(strValue, out tmp);
                if (tmp > 0 && tmp < 65536)
                {
                    mySqlPort = strValue;
                }
                else
                {
                    this.toConsole(1, "Invalid SQL Port Value.");
                }
            }
            else if (strVariable.Contains("MySQL Database") && !strVariable.Contains("ipp"))
            {
                mySqlDatabase = strValue.Trim();
            }
            else if (strVariable.Contains("MySQL Username") && !strVariable.Contains("ipp"))
            {
                mySqlUsername = strValue.Trim();
            }
            else if (strVariable.Contains("MySQL Password") && !strVariable.Contains("ipp"))
            {
                mySqlPassword = strValue.Trim();
            }
            else if (strVariable.Contains("Big Table") && !strVariable.Contains("ipp"))
            {
                bigTableName = strValue.Trim();
            }
            else if (strVariable.Contains("Day Table") && !strVariable.Contains("ipp"))
            {
                dayTableName = strValue.Trim();
            }
            else if (strVariable.Contains("MySQL Hostname") && strVariable.Contains("ipp"))
            {
                this.toConsole(3, "pl2_mySqlHostname set!");
                pl2_mySqlHostname = strValue.Trim();
            }
            else if (strVariable.Contains("MySQL Port") && strVariable.Contains("ipp"))
            {
                int tmp = 3306;
                int.TryParse(strValue, out tmp);
                if (tmp > 0 && tmp < 65536)
                {
                    pl2_mySqlPort = strValue;
                }
                else
                {
                    this.toConsole(1, "Invalid SQL Port Value.");
                }
            }
            else if (strVariable.Contains("MySQL Database") && strVariable.Contains("ipp"))
            {
                pl2_mySqlDatabase = strValue.Trim();
            }
            else if (strVariable.Contains("MySQL Username") && strVariable.Contains("ipp"))
            {
                pl2_mySqlUsername = strValue.Trim();
            }
            else if (strVariable.Contains("MySQL Password") && strVariable.Contains("ipp"))
            {
                pl2_mySqlPassword = strValue.Trim();
            }
            else if (strVariable.Contains("ipp Table Name") && strVariable.Contains("ipp"))
            {
                pl2_ippTableName = strValue.Trim();
            }
            else if (strVariable.Contains("ipp Server/Event Key"))
            {
                pl2_eventKey = strValue.Trim();
            }
            else if (strVariable.Contains("Debug Level"))
            {
                try
                {
                    debugLevel = Int32.Parse(strValue);
                }
                catch (Exception z)
                {
                    toConsole(1, "Invalid debug level! Choose 0, 1, or 2 only.");
                    debugLevel = 1;
                }
            }
        }
    }
}