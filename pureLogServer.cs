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

        public pureLogServer()
        {

        }

        public void output(object source, ElapsedEventArgs e)
        {
            if (pluginEnabled)
            {
                //this.toChat("pureLog Server Tracking " + playerCount + " players online.");
                this.toConsole("pureLog Server Tracking " + playerCount + " players online.");
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
                    this.toConsole("Couldn't open query connection! The last interval could not be saved.");
                    this.toConsole(m.ToString());
                    abortUpdate = true;
                }
                try { query.ExecuteNonQuery(); }
                catch (Exception m)
                {
                    this.toConsole("Couldn't parse query!");
                    this.toConsole(m.ToString());
                    abortUpdate = true;
                }
                query.Connection.Close();
                if (!abortUpdate)
                {
                    toConsole("Added an interval worth " + playerCount);
                }
            }
        }

        //The first thing the plugin does.
        public void establishFirstConnection()
        {
            this.toConsole("Trying to connect to " + mySqlHostname + ":" + mySqlPort + " with username " + mySqlUsername);
            this.firstConnection = new MySqlConnection("Server=" + mySqlHostname + ";" + "Port=" + mySqlPort + ";" + "Database=" + mySqlDatabase + ";" + "Uid=" + mySqlUsername + ";" + "Pwd=" + mySqlPassword + ";" + "Connection Timeout=5;");
            try{ this.firstConnection.Open(); }
            catch (Exception e)
            {
                this.toConsole(e.ToString());
                this.SqlConnected = false;
            }
            //Get ready to rock!
            if (SqlConnected)
            {
                this.firstConnection.Close();
                this.toConsole("Connection established with " + mySqlHostname + "!");
                confirmedConnection = firstConnection;
                this.updateTimer = new Timer();
                this.updateTimer.Elapsed += new ElapsedEventHandler(this.output);
                this.updateTimer.Interval = intervalLength;
                this.updateTimer.Start();
                //this.output();
            }
            else
            {
                this.toConsole("Could not establish an initial connection. Check the plugin settings and restart the plugin and/or Procon layer.");
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
                this.toConsole("Couldn't open query connection! The plugin will assume it is NOT a new day.");
                this.toConsole(e.ToString());
            }
            try{ rowCount = int.Parse(query.ExecuteScalar().ToString()); }
            catch (Exception e)
            {
                this.toConsole("Couldn't parse query!");
                this.toConsole(e.ToString());
            }
            query.Connection.Close();

            //There are no rows with today's date in the big table. Must be a new day!
            if (rowCount == 0)
            {
                bool abortUpdate = false;

                toConsole("Today is " + dateNow + ". Good morning!");
                toConsole("Summing up yesterday's player minutes...");

                //Adding up yesterday's minutes...
                int minSum = 0;
                query = new MySqlCommand("SELECT SUM(min) FROM " + dayTableName, this.confirmedConnection);
                try{ query.Connection.Open(); }
                catch (Exception e)
                {
                    this.toConsole("Couldn't open query connection! Couldn't sum up yesterday!.");
                    this.toConsole(e.ToString());
                    abortUpdate = true;
                }
                try { minSum = int.Parse(query.ExecuteScalar().ToString()); }
                catch (Exception e)
                {
                    this.toConsole("Couldn't parse query!");
                    this.toConsole(e.ToString());
                    abortUpdate = true;
                }
                query.Connection.Close();
                toConsole("Yesterday's sum was " + minSum + " player minutes.");

                if (!abortUpdate)
                {
                    //Update yesterday's minutes...
                    query = new MySqlCommand("UPDATE " + bigTableName + " SET min=" + minSum + " WHERE date='" + dateYesterday + "'", this.confirmedConnection);
                    try { query.Connection.Open(); }
                    catch (Exception e)
                    {
                        this.toConsole("Couldn't open query connection! Yesterday's big table entry was not updated.");
                        this.toConsole(e.ToString());
                        abortUpdate = true;
                    }
                    try { query.ExecuteNonQuery(); }
                    catch (Exception e)
                    {
                        this.toConsole("Couldn't parse query!");
                        this.toConsole(e.ToString());
                        abortUpdate = true;
                    }
                    query.Connection.Close();
                    

                    if (!abortUpdate)
                    {

                        toConsole("Updated yesterday's minutes!");
                        //and finally, start a new day
                        query = new MySqlCommand("INSERT INTO " + bigTableName + " (date) VALUES ('" + dateNow + "')", this.confirmedConnection);
                        try { query.Connection.Open(); }
                        catch (Exception e)
                        {
                            this.toConsole("Couldn't open query connection! Big table does not have an entry for today!");
                            this.toConsole(e.ToString());
                            abortUpdate = true;
                        }
                        try { query.ExecuteNonQuery(); }
                        catch (Exception e)
                        {
                            this.toConsole("Couldn't parse query!");
                            this.toConsole(e.ToString());
                            abortUpdate = true;
                        }
                        query.Connection.Close();

                        if (!abortUpdate)
                        {
                            toConsole("New big table row inserted!");
                            //clear the day table for a new day
                            //and finally, start a new day
                            query = new MySqlCommand("DELETE FROM " + dayTableName, this.confirmedConnection);
                            try { query.Connection.Open(); }
                            catch (Exception e)
                            {
                                this.toConsole("Couldn't open query connection! Day Table NOT cleared!");
                                this.toConsole(e.ToString());
                                abortUpdate = true;
                            }
                            try { query.ExecuteNonQuery(); }
                            catch (Exception e)
                            {
                                this.toConsole("Couldn't parse query!");
                                this.toConsole(e.ToString());
                                abortUpdate = true;
                            }
                            query.Connection.Close();
                            if (!abortUpdate)
                            {
                                toConsole("Day table reset!");
                                //clear the day table for a new day

                            }


                        }
                    }
                }
            }
            else
            {
                toConsole("Same day as usual.");
            }
        }

        //---------------------------------------------------
        //Helper functions
        //---------------------------------------------------

        public void toChat(String message)
        {
            this.ExecuteCommand("procon.protected.send", "admin.say", "pureLogS: " + message, "all");
        }

        public void toConsole(String message)
        {
            this.ExecuteCommand("procon.protected.pluginconsole.write", "pureLogS: " + message);
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
            return "0.5.4";
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
            return @"<p>Attempts to connect to an MySQL database, and then states the player count in the chatbox once every minute while checking the date on the database and inserting a new row if necessary. Make sure the MySQL server is accepting remote connections.</p>
                    <p><b>Initial Setup: </b><br/>Create a table in the database (Big Table) with three columns: id (INT), date (VARCHAR 255), and min (INT). Set id to auto-increment.<br/>
                    Make another table in the database (Day Table) with three columns: id (INT), time (VARCHAR 255), and min (INT). Set id to auto-increment.<br/>
                    Fill out ALL plugin settings before starting the plugin. Use an IP address for hostname and. The default port is 3306.</p>
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
            this.toConsole("pureLog Server Edition Running");
            this.establishFirstConnection();
        }

        public void OnPluginDisable()
        {
            this.pluginEnabled = false;
            this.ExecuteCommand("procon.protected.tasks.remove", "pureLogServer");
            this.updateTimer.Stop();
            this.toConsole("pureLog Server Edition Closed");
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

        }
    }
}