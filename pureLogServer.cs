//TODO
//Run check for day table clear so there's no double entries
//Clean up redundant stuff (make a big function for everything)
//


using System;
using System.IO;
using System.Timers;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;

using PRoCon.Core;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;
using PRoCon.Core.Players;

namespace PRoConEvents
{
    public class pureLogServer : PRoConPluginAPI, IPRoConPluginInterface
    {

        private bool pluginEnabled = true;
        private int playerCount;
        private Timer updateTimer;
        private String mySqlHostname;
        private String mySqlPort;
        private String mySqlDatabase;
        private String mySqlUsername;
        private String mySqlPassword;
        private int intervalLength = 60000;

        private MySqlConnection firstConnection;
        private MySqlConnection confirmedConnection;
        private bool SqlConnected = true;
        private String bigTableName;
        private String dayTableName;
        private String debugLevelString = "1";
        private int debugLevel = 1;

        public pureLogServer()
        {

        }

        public void output(object source, ElapsedEventArgs e)
        {
            if (pluginEnabled)
            {
                //this.toChat("pureLog Server Tracking " + playerCount + " players online.");
                this.toConsole(2, "pureLog Server Tracking " + playerCount + " players online.");
                this.goodMorning();

                bool abortUpdate = false;
                /*DateTime Now = DateTime.Now;
                int nowMinutes = Now.Minute;
                //workaround to get 24 hours, there's probably an easier way to do this
                int nowHour = Convert.ToInt32(Now.ToString("H"));
                nowMinutes = nowMinutes + (nowHour*60);*/

                //Insert the latest interval
                MySqlCommand query = new MySqlCommand("INSERT INTO " + dayTableName + " (min) VALUES ('" + playerCount + "')", this.confirmedConnection);
                try { query.Connection.Open(); }
                catch (Exception m)
                {
                    this.toConsole(1, "Couldn't open query connection! The last interval could not be saved.");
                    this.toConsole(1, m.ToString());
                    abortUpdate = true;
                }
                try { query.ExecuteNonQuery(); }
                catch (Exception m)
                {
                    this.toConsole(1, "Couldn't parse query!");
                    this.toConsole(1, m.ToString());
                    abortUpdate = true;
                }
                query.Connection.Close();
                if (!abortUpdate)
                {
                    toConsole(2, "Added an interval worth " + playerCount);
                }
            }
        }

        //The first thing the plugin does.
        public void establishFirstConnection()
        {
            this.toConsole(2, "Trying to connect to " + mySqlHostname + ":" + mySqlPort + " with username " + mySqlUsername);
            this.firstConnection = new MySqlConnection("Server=" + mySqlHostname + ";" + "Port=" + mySqlPort + ";" + "Database=" + mySqlDatabase + ";" + "Uid=" + mySqlUsername + ";" + "Pwd=" + mySqlPassword + ";" + "Connection Timeout=5;");
            try{ this.firstConnection.Open(); }
            catch (Exception e)
            {
                this.toConsole(1, e.ToString());
                this.SqlConnected = false;
            }
            //Get ready to rock!
            if (SqlConnected)
            {
                this.firstConnection.Close();
                this.toConsole(1, "Connection established with " + mySqlHostname + "!");
                confirmedConnection = firstConnection;
                this.updateTimer = new Timer();
                this.updateTimer.Elapsed += new ElapsedEventHandler(this.output);
                this.updateTimer.Interval = intervalLength;
                this.updateTimer.Start();
                //this.output();
            }
            else
            {
                this.toConsole(1, "Could not establish an initial connection. Check the plugin settings and restart the plugin and/or Procon layer.");
            }
        }

        //Is it a new day?
        public void goodMorning()
        {
            String dateNow = DateTime.Now.ToString("MMddyyyy");
            String dateYesterday = DateTime.Now.AddDays(-1).ToString("MMddyyyy");

            int rowCount = 999;
            MySqlCommand query = new MySqlCommand("SELECT COUNT(*) FROM " + bigTableName + " WHERE date='" + dateNow + "'", this.confirmedConnection);
            try{ query.Connection.Open(); }
            catch (Exception e)
            {
                this.toConsole(1, "Couldn't open query connection! The plugin will assume it is NOT a new day.");
                this.toConsole(1, e.ToString());
            }
            try{ rowCount = int.Parse(query.ExecuteScalar().ToString()); }
            catch (Exception e)
            {
                this.toConsole(1, "Couldn't parse query!");
                this.toConsole(1, e.ToString());
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
                try{ query.Connection.Open(); }
                catch (Exception e)
                {
                    this.toConsole(1, "Couldn't open query connection! Couldn't sum up yesterday!.");
                    this.toConsole(1, e.ToString());
                    abortUpdate = true;
                }
                try { minSum = int.Parse(query.ExecuteScalar().ToString()); }
                catch (Exception e)
                {
                    this.toConsole(1, "Couldn't parse query!");
                    this.toConsole(1, e.ToString());
                    abortUpdate = true;
                }
                query.Connection.Close();
                this.toConsole(1, "Yesterday's sum was " + minSum + " player minutes.");

                if (!abortUpdate)
                {
                    //Update yesterday's minutes...
                    query = new MySqlCommand("UPDATE " + bigTableName + " SET min=" + minSum + " WHERE date='" + dateYesterday + "'", this.confirmedConnection);
                    try { query.Connection.Open(); }
                    catch (Exception e)
                    {
                        this.toConsole(1, "Couldn't open query connection! Yesterday's big table entry was not updated.");
                        this.toConsole(1, e.ToString());
                        abortUpdate = true;
                    }
                    try { query.ExecuteNonQuery(); }
                    catch (Exception e)
                    {
                        this.toConsole(1, "Couldn't parse query!");
                        this.toConsole(1, e.ToString());
                        abortUpdate = true;
                    }
                    query.Connection.Close();
                    

                    if (!abortUpdate)
                    {

                        this.toConsole(2, "Updated yesterday's minutes!");
                        //and finally, start a new day
                        query = new MySqlCommand("INSERT INTO " + bigTableName + " (date) VALUES ('" + dateNow + "')", this.confirmedConnection);
                        try { query.Connection.Open(); }
                        catch (Exception e)
                        {
                            this.toConsole(1, "Couldn't open query connection! Big table does not have an entry for today!");
                            this.toConsole(1, e.ToString());
                            abortUpdate = true;
                        }
                        try { query.ExecuteNonQuery(); }
                        catch (Exception e)
                        {
                            this.toConsole(1, "Couldn't parse query!");
                            this.toConsole(1, e.ToString());
                            abortUpdate = true;
                        }
                        query.Connection.Close();

                        if (!abortUpdate)
                        {
                            this.toConsole(2, "New big table row inserted!");
                            //clear the day table for a new day
                            //and finally, start a new day
                            query = new MySqlCommand("DELETE FROM " + dayTableName, this.confirmedConnection);
                            try { query.Connection.Open(); }
                            catch (Exception e)
                            {
                                this.toConsole(1, "Couldn't open query connection! Day Table NOT cleared!");
                                this.toConsole(1, e.ToString());
                                abortUpdate = true;
                            }
                            try { query.ExecuteNonQuery(); }
                            catch (Exception e)
                            {
                                this.toConsole(1, "Couldn't parse query!");
                                this.toConsole(1, e.ToString());
                                abortUpdate = true;
                            }
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

        //---------------------------------------------------
        // Attributes
        //---------------------------------------------------

        public string GetPluginName()
        {
            return "pureLog Server Edition";
        }
        public string GetPluginVersion()
        {
            return "0.6.3";
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
            return @"<p>Updates a MySQL database with daily player count logging. Make sure the MySQL server is accepting remote connections.</p>
                    <p><b>Initial Setup: </b><br/>Create a table in the database (Big Table) with three columns: id (INT), date (VARCHAR 255), and min (INT). Set id to auto-increment.<br/>
                    Make another table in the database (Day Table) with three columns: id (INT), time (VARCHAR 255), and min (INT). Set id to auto-increment.<br/>
                    Fill out ALL plugin settings before starting the plugin. Use an IP address for hostname and. The default port is 3306.<br/>
                    The debug levels are as follows: 0 suppresses ALL messages (not recommended), 1 shows important messages only (recommended), and 2 shows ALL messages (useful for step by step debugging).</p>
                    <p><b>Troubleshooting: </b><br/>Currently there is a bug where the plugin fails to connect if it was running before a Procon layer restart. To get it working again, disable the plugin, restart the Procon layer, and enable the plugin.
                    <br/>The IP address that Procon uses to access the MySQL server is different from the IP of the layer itself. If a remote connection can't be established, try using more wildcards in the accepted connections (do %.%.%.% to test).</p>
                    <p><b>Fallbacks: </b><br/>If at any step in the table updating process the connection fails, the plugin will continue adding minutes to the most recent day. A new day for the plugin only begins when the connection is successful.
                    <br/>If there is no entry for yesterday, the plugin will assume this is a first run and drop whatever it had initially. A future version of this plugin will repair missing calendar days.</p>
                    ";
        }

        //---------------------------------------------------
        // Config (totally not a ripoff)
        //---------------------------------------------------
        public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
        {
            this.RegisterEvents(this.GetType().Name, "OnPluginLoaded", "OnServerInfo");
        }

        public void OnPluginEnable()
        {
            this.pluginEnabled = true;
            this.toConsole(1, "pureLog Server Edition Running");
            this.establishFirstConnection();
        }

        public void OnPluginDisable()
        {
            this.pluginEnabled = false;
            this.ExecuteCommand("procon.protected.tasks.remove", "pureLogServer");
            this.updateTimer.Stop();
            this.toConsole(1, "pureLog Server Edition Closed");
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
                    this.ExecuteCommand("procon.protected.pluginconsole.write", "Invalid SQL Port Value");
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
                try{
                    debugLevel = Int32.Parse(debugLevelString);
                }catch (Exception z){
                    toConsole(1,  "Invalid debug level! Choose 0, 1, or 2 only.");
                    debugLevel = 1;
                }
            }
        }
    }
}