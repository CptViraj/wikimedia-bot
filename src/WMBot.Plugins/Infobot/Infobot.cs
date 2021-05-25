//  This program is free software; you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation; either version 2 of the License, or   
//  (at your option) version 3.                                         

//  This program is distributed in the hope that it will be useful,     
//  but WITHOUT ANY WARRANTY; without even the implied warranty of      
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the       
//  GNU General Public License for more details.                        

//  You should have received a copy of the GNU General Public License   
//  along with this program; if not, write to the                       
//  Free Software Foundation, Inc.,                                     
//  51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;

namespace wmib.Extensions
{
    public partial class Infobot
    {
        public string DatafileRAW = "";
        public string DatafileXML = "";
        private string temporaryData = "";
        public bool Sensitive = true;
        public bool stored = true;
        public static string DefaultPrefix = "!";
        public string prefix = "!";

        private Thread tSearch;
        public Thread SnapshotManager = null;
        private readonly Module Parent;

        // if we need to update dump
        public bool update = true;
        public static Channel ReplyChan = null;
        public static DateTime NA = DateTime.MaxValue;
        /// <summary>
        /// List of all items in class
        /// </summary>
        public List<InfobotKey> Keys = new List<InfobotKey>();

        /// <summary>
        /// List of all aliases we want to use
        /// </summary>
        public List<InfobotAlias> Aliases = new List<InfobotAlias>();
        public Channel pChannel;
        private string search_key;

        public Infobot(string database, Channel channel, Module module, bool sensitive = true)
        {
            Sensitive = sensitive;
            DatafileXML = database + ".xml";
            DatafileRAW = database;
            pChannel = channel;
            Parent = module;
            prefix = Module.GetConfig(pChannel, "Infobot.Prefix", DefaultPrefix);
            LoadData();
        }

        public bool AliasExists(string name, bool sensitive = true)
        {
            if (sensitive)
            {
                lock (this)
                {
                    foreach (InfobotAlias key in Aliases)
                    {
                        if (key.Name == name)
                            return true;
                    }
                }
            }
            if (!sensitive)
            {
                name = name.ToLower();
                lock (this)
                {
                    foreach (InfobotAlias key in Aliases)
                    {
                        if (key.Name.ToLower() == name)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Function returns true if key exists
        /// </summary>
        /// <param name="name">Name of key</param>
        /// <param name="sensitive">If bot is sensitive or not</param>
        /// <returns></returns>
        public bool KeyExists(string name, bool sensitive = true)
        {
            if (!sensitive)
            {
                name = name.ToLower();
                lock (this)
                {
                    foreach (InfobotKey key in Keys)
                    {
                        if (key.Key.ToLower() == name)
                            return true;
                    }
                }
            }
            if (sensitive)
            {
                lock (this)
                {
                    foreach (InfobotKey key in Keys)
                    {
                        if (key.Key == name)
                            return true;
                    }
                }
            }
            return false;
        }

        public InfobotKey GetKey(string name, bool sensitive = true)
        {
            if (!sensitive)
            {
                lock (this)
                {
                    name = name.ToLower();
                    foreach (InfobotKey key in Keys)
                    {
                        if (key.Key.ToLower() == name)
                            return key;
                    }
                }
            }
            if (sensitive)
            {
                lock (this)
                {
                    foreach (InfobotKey key in Keys)
                    {
                        if (key.Key == name)
                            return key;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// @infobot-detail
        /// </summary>
        /// <param name="key"></param>
        /// <param name="chan"></param>
        public void InfobotDetail(string key, Channel chan)
        {
            InfobotKey CV = GetKey(key, Sensitive);
            if (CV == null)
            {
                IRC.DeliverMessage("There is no such a key", chan, libirc.Defs.Priority.Low);
                return;
            }
            if (CV.Key == key)
            {
                string created = "N/A";
                string last = "N/A";
                string name = "N/A";
                if (CV.LastTime != NA)
                {
                    TimeSpan span = DateTime.Now - CV.LastTime;
                    last = CV.LastTime + " (" + span + " ago)";
                }
                if (CV.CreationTime != NA)
                    created = CV.CreationTime.ToString();
                if (!string.IsNullOrEmpty(CV.User))
                    name = CV.User;
                string type = " this key is not raw (variables will be changed using some awesome magic)";
                if (CV.Raw)
                {
                    type = " this key is raw";
                }
                IRC.DeliverMessage(Localization.Localize("infobot-data", chan.Language, new List<string> {key, name, created, CV.Displayed.ToString(),
                        last + type }), chan, libirc.Defs.Priority.Low);
            }
        }

        public List<InfobotKey> SortedItem()
        {
            List<InfobotKey> OriginalList = new List<InfobotKey>();
            List<InfobotKey> Item = new List<InfobotKey>();
            int keycount;
            lock (this)
            {
                keycount = Keys.Count;
                OriginalList.AddRange(Keys);
            }
            try
            {
                if (keycount > 0)
                {
                    List<string> Name = new List<string>();
                    foreach (InfobotKey curr in OriginalList)
                    {
                        Name.Add(curr.Key);
                    }
                    Name.Sort();
                    foreach (string f in Name)
                    {
                        foreach (InfobotKey g in OriginalList)
                        {
                            if (f == g.Key)
                            {
                                Item.Add(g);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception fail)
            {
                Parent.HandleException(fail);
                Parent.Log("Exception while creating list for html");
            }
            return Item;
        }

        private static string ParseInfo(List<string> parameters, string original, InfobotKey Key, libirc.UserInfo fu)
        {
            bool raw = false;
            if (Key != null)
                raw = Key.Raw;
            string text = Key.Text;
            if (!raw)
            {
                text = text.Replace("$infobot_nick", fu.Nick);
                text = text.Replace("$infobot_host", fu.Host);
            }
            if (parameters.Count > 0)
            {
                string keys = "";
                int curr = 0;
                while (parameters.Count > curr)
                {
                    if (!raw)
                    {
                        text = text.Replace("$" + (curr+1), parameters[curr]);
                        text = text.Replace("$url_encoded_" + (curr+1), HttpUtility.UrlEncode(parameters[curr]));
                        text = text.Replace("$wiki_encoded_" + (curr+1), HttpUtility.UrlEncode(parameters[curr]).Replace("+", "_").Replace("%3a", ":").Replace("%2f", "/").Replace("%28", "(").Replace("%29", ")"));
                    }
                    if (keys == "")
                    {
                        keys = parameters[curr];
                    }
                    else
                    {
                        keys = keys + " " + parameters[curr];
                    }
                    curr++;
                }
                if (original.Contains ("|") && !raw)
                {
                    original = original.Substring (0, original.IndexOf ("|", StringComparison.InvariantCulture));
                    original = original.Trim ();
                }
                text = text.Replace("$*", original);
                text = text.Replace("$url_encoded_*", HttpUtility.UrlEncode(original));
                text = text.Replace("$wiki_encoded_*", HttpUtility.UrlEncode(original).Replace("+", "_").Replace("%3a", ":").Replace("%2f", "/").Replace("%28", "(").Replace("%29", ")"));
            }
            return text;
        }

        public static bool Linkable(Channel host, Channel guest)
        {
            if (host == null || guest == null)
            {
                return false;
            }
            return host.SharedLinkedChan.Contains(guest);
        }

        /// <summary>
        /// Determines whether this key is ignored for channel
        /// </summary>
        /// <returns>
        /// <c>true</c> if this instance is ignored the specified name; otherwise, <c>false</c>.
        /// </returns>
        /// <param name='name'>
        /// If set to <c>true</c> name.
        /// </param>
        /// <param name="channel"></param>
        public bool IsIgnored(string name, Channel channel)
        {
            string ignore_test = name;
            if (ignore_test.Contains(" "))
            {
                ignore_test = ignore_test.Substring(0, ignore_test.IndexOf(" ", StringComparison.InvariantCulture));
            }
            return (channel.Infobot_IgnoredNames.Contains(ignore_test));
        }

        private bool DeliverKey(InfobotKey Key, string OriginalText, Channel chan, libirc.UserInfo fu)
        {
            if (Key == null)
            {
                return false;
            }
            string Target_ = "";
            string text = OriginalText;
            // we remove the key name from message so that only parameters remain
            if (text.Contains(" "))
                text = text.Substring(text.IndexOf(" ", StringComparison.InvariantCulture) + 1);
            else
                text = "";
            if (text.Contains("|"))
            {
                Target_ = OriginalText.Substring(OriginalText.IndexOf("|", StringComparison.InvariantCulture) + 1);
                if (Module.GetConfig(chan, "Infobot.Trim-white-space-in-name", true))
                {
                    Target_ = Target_.Trim();
                }
                text = text.Substring(0, text.IndexOf("|", StringComparison.InvariantCulture));
            }
            List<string> Parameters = new List<string>(text.Split(' '));
            string value_ = ParseInfo(Parameters, text, Key, fu);
            if (Key.IsAct)
            {
                if (String.IsNullOrEmpty(Target_))
                    IRC.DeliverAction(value_, chan);
                else
                    IRC.DeliverAction(Target_ + ": " + value_, chan);
            }
            else
            {
                if (String.IsNullOrEmpty(Target_))
                    IRC.DeliverMessage(value_, chan);
                else
                    IRC.DeliverMessage(Target_ + ": " + value_, chan);
            }
            Key.Displayed++;
            Key.LastTime = DateTime.Now;
            this.StoreDB();
            return true;
        }

        /// <summary>
        /// Print a value to channel if found, this message doesn't need to be a valid command for it to work
        /// </summary>
        /// <param name="message">Message</param>
        /// <param name="user">User</param>
        /// <param name="chan">Channel</param>
        /// <returns></returns>
        public bool InfobotExec(string message, libirc.UserInfo user, Channel chan)
        {
            try
            {
                // check if it starts with the prefix
                if (!message.StartsWith(prefix, StringComparison.InvariantCulture))
                {
                    return true;
                }
                // check if this channel is allowed to access the db
                Channel data = RetrieveMasterDBChannel(chan);
                bool Allowed = (data != null);
                // handle prefix
                message = message.Substring(1);
                Infobot infobot = null;

                if (Allowed)
                    infobot = (Infobot)data.RetrieveObject("Infobot");

                // check if key is ignored
                if (IsIgnored(message, chan))
                    return true;

                // split by parameters so we can easily get the arguments user provided
                List<string> Parameters = new List<string>(message.Split(' '));

                // check if key has some parameters or command
                if (Parameters.Count > 1)
                {
                    // someone want to create a new key
                    if (Parameters[1] == "is" || Parameters[1] == "act")
                    {
                        bool isAct = Parameters[1] == "act";
                        // check if they are approved to do that
                        if (chan.SystemUsers.IsApproved(user, InfobotModule.PermissionAdd))
                        {
                            if (!Allowed)
                            {
                                // check if we can deliver error message
                                if (!chan.SuppressWarnings)
                                    IRC.DeliverMessage(Localization.Localize("db7", chan.Language), chan);
                                return true;
                            }
                            // they can but there is only 1 parameter and we need at least 2
                            if (Parameters.Count < 3)
                            {
                                if (!chan.SuppressWarnings)
                                    IRC.DeliverMessage(Localization.Localize("key", chan.Language), chan);
                                return true;
                            }
                            // get a key name
                            string key;
                            if (!isAct)
                            {
                                key = message.Substring(message.IndexOf(" is", StringComparison.InvariantCulture) + 4);
                            }
                            else
                            {
                                key = message.Substring(message.IndexOf(" act", StringComparison.InvariantCulture) + 5);
                            }
                            if (infobot != null)
                            {
                                infobot.SetKey(key, Parameters[0], user.Nick, chan, isAct);
                                return true;
                            }
                        }
                        else
                        {
                            if (!chan.SuppressWarnings)
                                IRC.DeliverMessage(Localization.Localize("Authorization", chan.Language), chan);
                        }
                        return false;
                    } else if (Parameters[1] == "replace")
                    {
                        // check if they are approved to do that
                        if (chan.SystemUsers.IsApproved(user, InfobotModule.PermissionAdd))
                        {
                            if (!Allowed)
                            {
                                // check if we can deliver error message
                                if (!chan.SuppressWarnings)
                                    IRC.DeliverMessage(Localization.Localize("db7", chan.Language), chan);
                                return true;
                            }
                            // they can but there is only 1 parameter and we need at least 2
                            if (Parameters.Count < 3)
                            {
                                if (!chan.SuppressWarnings)
                                    IRC.DeliverMessage(Localization.Localize("key", chan.Language), chan);
                                return true;
                            }
                            // get a key name
                            string key = message.Substring(message.IndexOf(" replace", StringComparison.InvariantCulture) + 9);
                            if (infobot != null)
                            {
                                infobot.replaceKey(key, Parameters[0], user.Nick, chan);
                                return true;
                            }
                        }
                        else if (!chan.SuppressWarnings)
                        {
                            IRC.DeliverMessage(Localization.Localize("Authorization", chan.Language), chan);
                        }
                        return false;
                    }
                    // alias
                    bool force = false;
                    if (Parameters[1] == "alias" || Parameters[1] == "force-alias")
                    {
                        if (Parameters[1] == "force-alias")
                        {
                            force = true;
                        }
                        if (chan.SystemUsers.IsApproved(user, InfobotModule.PermissionAdd))
                        {
                            if (!Allowed)
                            {
                                if (!chan.SuppressWarnings)
                                {
                                    IRC.DeliverMessage(Localization.Localize("db7", chan.Language), chan);
                                }
                                return true;
                            }
                            if (Parameters.Count < 3)
                            {
                                if (!chan.SuppressWarnings)
                                {
                                    IRC.DeliverMessage(Localization.Localize("InvalidAlias", chan.Language), chan);
                                }
                                return true;
                            }
                            if (infobot != null)
                            {
                                infobot.aliasKey(message.Substring(message.IndexOf(" alias", StringComparison.InvariantCulture) + 7), Parameters[0], "", chan, force);
                                return true;
                            }
                        }
                        else
                        {
                            if (!chan.SuppressWarnings)
                            {
                                IRC.DeliverMessage(Localization.Localize("Authorization", chan.Language), chan);
                            }
                        }
                        return false;
                    }
                    if (Parameters[1] == "unalias")
                    {
                        if (chan.SystemUsers.IsApproved(user, InfobotModule.PermissionDel))
                        {
                            if (!Allowed)
                            {
                                if (!chan.SuppressWarnings)
                                {
                                    IRC.DeliverMessage(Localization.Localize("db7", chan.Language), chan);
                                }
                                return true;
                            }
                            if (infobot != null)
                            {
                                lock (infobot)
                                {
                                    foreach (InfobotAlias b in infobot.Aliases)
                                    {
                                        if (b.Name == Parameters[0])
                                        {
                                            infobot.Aliases.Remove(b);
                                            IRC.DeliverMessage(Localization.Localize("AliasRemoved", chan.Language), chan);
                                            this.StoreDB();
                                            return false;
                                        }
                                    }
                                }
                            }
                            return false;
                        }
                        if (!chan.SuppressWarnings)
                        {
                            IRC.DeliverMessage(Localization.Localize("Authorization", chan.Language), chan);
                        }
                        return false;
                    }
                    // remove key
                    if (Parameters[1] == "del")
                    {
                        if (chan.SystemUsers.IsApproved(user, InfobotModule.PermissionDel))
                        {
                            if (!Allowed)
                            {
                                IRC.DeliverMessage(Localization.Localize("db7", chan.Language), chan);
                                return true;
                            }
                            if (infobot != null)
                            {
                                infobot.rmKey(Parameters[0], "", chan);
                            }
                        }
                        else
                        {
                            if (!chan.SuppressWarnings)
                            {
                                IRC.DeliverMessage(Localization.Localize("Authorization", chan.Language), chan);
                            }
                        }
                        return false;
                    }
                }
                if (!Allowed)
                {
                    return true;
                }

                InfobotKey Key = infobot.GetKey(Parameters[0]);
                // let's try to deliver this as a key
                if (DeliverKey(Key, message, chan, user))
                {
                    return true;
                }
                
                string lower = Parameters[0].ToLower();
                // there is no key with this name, let's check if there is an alias for such a key
                lock (infobot)
                {
                    foreach (InfobotAlias alias in infobot.Aliases)
                    {
                        if (Sensitive)
                        {
                            if (alias.Name == Parameters[0])
                            {
                                // let's try to get a target key
                                InfobotKey Key_ = infobot.GetKey(alias.Key);
                                if (DeliverKey(Key_, message, chan, user))
                                {
                                    return true;
                                }
                            }
                        }
                        else
                        {
                            if (alias.Name.ToLower() == lower)
                            {
                                // let's try to get a target key
                                InfobotKey Key_ = infobot.GetKey(alias.Key);
                                if (DeliverKey(Key_, message, chan, user))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }

                if (Module.GetConfig(chan, "Infobot.auto-complete", false))
                {
                    if (infobot != null)
                    {
                        List<string> results = new List<string>();
                        lock (infobot)
                        {
                            foreach (InfobotKey f in infobot.Keys)
                            {
                                if (!results.Contains(f.Key) && f.Key.StartsWith(Parameters[0], StringComparison.InvariantCulture))
                                {
                                    results.Add(f.Key);
                                }
                            }
                            foreach (InfobotAlias f in infobot.Aliases)
                            {
                                if (!results.Contains(f.Key) && f.Key.StartsWith(Parameters[0], StringComparison.InvariantCulture))
                                {
                                    results.Add(f.Key);
                                }
                            }
                        }

                        if (results.Count == 1)
                        {
                            InfobotKey Key_ = infobot.GetKey(results[0]);
                            if (DeliverKey(Key_, message, chan, user))
                            {
                                return true;
                            }
                            lock (infobot)
                            {
                                foreach (InfobotAlias alias in infobot.Aliases)
                                {
                                    if (alias.Name == results[0])
                                    {
                                        Key_ = infobot.GetKey(alias.Name);
                                        if (DeliverKey(Key_, message, chan, user))
                                        {
                                            return true;
                                        }
                                    }
                                }
                            }
                        }

                        if (results.Count > 1)
                        {
                            if (Module.GetConfig(chan, "Infobot.Sorted", false))
                            {
                                results.Sort();
                            }
                            string x = "";
                            foreach (string ix in results)
                            {
                                x += ix + ", ";
                            }
                            IRC.DeliverMessage(Localization.Localize("infobot-c-e", chan.Language, new List<string> { x }), chan);
                            return true;
                        }
                    }
                }

                if (Module.GetConfig(chan, "Infobot.Help", false) && infobot != null)
                {
                    List<string> Sugg = new List<string>();
                    string key = Parameters[0].ToLower();
                    lock (infobot)
                    {
                        foreach (InfobotKey f in infobot.Keys)
                        {
                            if (!Sugg.Contains(f.Key) && (f.Text.ToLower().Contains(key) || f.Key.ToLower().Contains(key)))
                            {
                                Sugg.Add(f.Key);
                            }
                        }
                    }

                    if (Sugg.Count > 0)
                    {
                        string x = "";
                        if (Module.GetConfig(chan, "Infobot.Sorted", false))
                        {
                            Sugg.Sort();
                        }
                        foreach (string a in Sugg)
                        {
                            x += "!" + a + ", ";
                        }
                        IRC.DeliverMessage(Localization.Localize("infobot-help", chan.Language, new List<string> { x }), chan.Name);
                        return true;
                    }
                }
            }
            catch (Exception b)
            {
                Parent.HandleException(b);
            }
            return true;
        }

        private void StartSearch()
        {
            Regex value = new Regex(search_key, RegexOptions.Compiled);
            Channel _channel = Core.GetChannel(pChannel.Name);
            string results = "";
            int count = 0;
            lock (this)
            {
                foreach (InfobotKey data in Keys)
                {
                    if (data.Key == search_key || value.Match(data.Text).Success)
                    {
                        count++;
                        results = results + data.Key + ", ";
                    }
                }
            }
            if (String.IsNullOrEmpty(results))
                IRC.DeliverMessage(Localization.Localize("ResultsWereNotFound", ReplyChan.Language), ReplyChan.Name);
            else
                IRC.DeliverMessage(Localization.Localize("Results", _channel.Language, new List<string> { count.ToString() }) + results, ReplyChan.Name);
            // ??
            InfobotModule.running = false;
        }

        /// <summary>
        /// Search
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="Chan"></param>
        public void RSearch(string key, Channel Chan)
        {
            if (key == Configuration.System.CommandPrefix + "regsearch")
            {
                IRC.DeliverMessage(Localization.Localize("Search1", Chan.Language), Chan.Name);
                return;
            }
            if (!key.StartsWith(Configuration.System.CommandPrefix + "regsearch ", StringComparison.InvariantCulture))
                return;
            if (!misc.IsValidRegex(key))
            {
                IRC.DeliverMessage(Localization.Localize("Error1", Chan.Language), Chan.Name);
                return;
            }
            if (key.Length < 12)
            {
                IRC.DeliverMessage(Localization.Localize("Search1", Chan.Language), Chan.Name);
                return;
            }
            Channel data = RetrieveMasterDBChannel(Chan);
            bool Allowed = (data != null);
            if (!Allowed)
            {
                IRC.DeliverMessage(Localization.Localize("db7", Chan.Language), Chan.Name);
                return;
            }
            Infobot infobot = (Infobot)data.RetrieveObject("Infobot");
            if (infobot == null)
            {
                Syslog.Log("Unable to perform regsearch because the Infobot doesn't exist in " + Chan.Name, true);
                return;
            }
            infobot.search_key = key.Substring(11);
            InfobotModule.running = true;
            ReplyChan = Chan;
            tSearch = new Thread(infobot.StartSearch);
            tSearch.Start();
            int check = 1;
            while (InfobotModule.running)
            {
                check++;
                Thread.Sleep(100);
                if (check > 8)
                {
                    tSearch.Abort();
                    IRC.DeliverMessage(Localization.Localize("Error2", Chan.Language), Chan.Name);
                    InfobotModule.running = false;
                    return;
                }
            }
        }

        public void Find(string key, Channel Chan)
        {
            if (Chan == null)
                return;

            if (key == Configuration.System.CommandPrefix + "search")
            {
                IRC.DeliverMessage(Localization.Localize("Search1", Chan.Language), Chan.Name);
                return;
            }

            if (!key.StartsWith(Configuration.System.CommandPrefix + "search ", StringComparison.InvariantCulture))
                return;

            Channel data = RetrieveMasterDBChannel(Chan);
            bool Allowed = (data != null);
            if (!Allowed)
            {
                IRC.DeliverMessage(Localization.Localize("db7", Chan.Language), Chan.Name);
                return;
            }
            if (key.Length < 8)
            {
                IRC.DeliverMessage(Localization.Localize("Search1", Chan.Language), Chan.Name);
                return;
            }
            key = key.Substring(8);
            int count = 0;
            Infobot infobot = (Infobot)data.RetrieveObject("Infobot");
            if (infobot == null)
            {
                Syslog.Log("Unable to perform search because the Infobot doesn't exist in " + Chan.Name, true);
                return;
            }
            string results = "";
            lock (infobot)
            {
                foreach (InfobotKey Data in infobot.Keys)
                {
                    if (Data.Key == key || Data.Text.Contains(key))
                    {
                        results = results + Data.Key + ", ";
                        count++;
                    }
                }
            }
            if (String.IsNullOrEmpty(results))
            {
                IRC.DeliverMessage(Localization.Localize("ResultsWereNotFound", Chan.Language), Chan.Name);
            }
            else
            {
                IRC.DeliverMessage(Localization.Localize("Results", Chan.Language, new List<string> { count.ToString() }) + results, Chan.Name);
            }
        }

        /// <summary>
        /// Retrieves the master DB channel
        /// </summary>
        /// <returns>
        /// The master DB channel.
        /// </returns>
        /// <param name='chan'>
        /// Chan.
        /// </param>
        private Channel RetrieveMasterDBChannel(Channel chan)
        {
            bool Allowed;
            Channel data = null;
            if (chan == null)
            {
                return null;
            }
            if (chan.SharedDB == "local" || chan.SharedDB == "")
            {
                data = chan;
                Allowed = true;
            }
            else
            {
                Allowed = Linkable(Core.GetChannel(chan.SharedDB), chan);
                if (Allowed)
                {
                    data = Core.GetChannel(chan.SharedDB);
                }
                if (data == null)
                {
                    Allowed = false;
                }
            }
            if (Allowed)
            {
                return data;
            }
            return null;
        }

        public void SetRaw(string key, string user, Channel chan)
        {
            InfobotKey Key = GetKey(key, Sensitive);
            if (Key == null)
            {
                IRC.DeliverMessage("There is no such a key, " + user, chan.Name);
                return;
            }
            Key.Raw = true;
            IRC.DeliverMessage("This key will be displayed with no extra styling, variables and will ignore all symbols", chan.Name);
            this.StoreDB();
        }

        public void UnsetRaw(string key, string user, Channel chan)
        {
            InfobotKey Key = GetKey(key, Sensitive);
            if (Key == null)
            {
                IRC.DeliverMessage("There is no such a key, " + user, chan.Name);
                return;
            }
            Key.Raw = false;
            IRC.DeliverMessage("This key will be displayed normally", chan.Name);
            this.StoreDB();
        }

        /// <summary>
        /// Save a new key
        /// </summary>
        /// <param name="Text">Text</param>
        /// <param name="key">Key</param>
        /// <param name="user">User who created it</param>
        /// <param name="chan"></param>
        /// <param name="isact"></param>
        public void SetKey(string Text, string key, string user, Channel chan, bool isact)
        {
            lock (this)
            {
                try
                {
                    if (KeyExists(key, Sensitive))
                    {
                        if (!chan.SuppressWarnings)
                        {
                            IRC.DeliverMessage(Localization.Localize("Error3", chan.Language), chan);
                        }
                        return;
                    }
                    Keys.Add(new InfobotKey(key, Text, user, "false", "", "", 0, false, isact));
                    IRC.DeliverMessage(Localization.Localize("infobot6", chan.Language), chan);
                    this.StoreDB();
                }
                catch (Exception b)
                {
                    Core.HandleException(b, "infobot");
                }
            }
        }

        public void SnapshotStart()
        {
            try
            {
                while (!this.stored)
                {
                    Thread.Sleep(100);
                }
                lock (this)
                {
                    DateTime creationdate = DateTime.Now;
                    Syslog.Log("Creating snapshot " + temporaryData);
                    File.Copy(DatafileXML, temporaryData);
                    IRC.DeliverMessage("Snapshot " + temporaryData + " was created for current database as of " + creationdate, pChannel);
                }
            }
            catch (Exception fail)
            {
                Syslog.Log("Unable to create a snapshot for " + pChannel.Name, true);
                Core.HandleException(fail, "infobot");
            }
        }

        public void RecoverStart()
        {
            try
            {
                while (!this.stored)
                {
                    Thread.Sleep(100);
                }
                lock (this)
                {
                    Syslog.Log("Recovering snapshot " + temporaryData);
                    File.Copy(temporaryData, DatafileXML, true);
                    this.Keys.Clear();
                    this.Aliases.Clear();
                    Parent.Log("Loading snapshot of " + pChannel.Name);
                    LoadData();
                    IRC.DeliverMessage("Snapshot " + temporaryData + " was loaded and previous database was permanently deleted", pChannel);
                }
            }
            catch (Exception fail)
            {
                Parent.Log("Unable to recover a snapshot for " + pChannel.Name + " the db is likely broken now", true);
                Parent.HandleException(fail);
            }
        }

        public bool IsValid(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (char i in name)
            {
                if (i == '\0')
                    continue;

                if (i < 48 || i > 122 || (i > 90 && i < 97) || (i > 57 && i < 65))
                    return false;
            }
            return true;
        }

        public void RecoverSnapshot(Channel chan, string name)
        {
            try
            {
                lock (this)
                {
                    if (!IsValid(name))
                    {
                        IRC.DeliverMessage("This is not a valid name for snapshot, you can only use a-zA-Z and 0-9 chars", chan.Name);
                        return;
                    }
                    if (SnapshotManager != null)
                    {
                        if (SnapshotManager.ThreadState == ThreadState.Running)
                        {
                            IRC.DeliverMessage("There is already another snapshot operation running for this channel", chan.Name);
                            return;
                        }
                    }
                    string datafile = InfobotModule.SnapshotsDirectory + Path.DirectorySeparatorChar + this.pChannel.Name + Path.DirectorySeparatorChar + name;
                    if (!File.Exists(datafile))
                    {
                        IRC.DeliverMessage("The requested datafile " + name + " was not found", chan.Name, libirc.Defs.Priority.Low);
                        return;
                    }

                    SnapshotManager = new Thread(RecoverStart);
                    temporaryData = datafile;
                    SnapshotManager.Name = "Module:Infobot/Snapshot";
                    Core.ThreadManager.RegisterThread(SnapshotManager);
                    SnapshotManager.Start();
                    Module.SetConfig(chan, "HTML.Update", true);
                }
            }
            catch (Exception fail)
            {
                Parent.HandleException(fail);
            }
        }

        /// <summary>
        /// Stores all data to database delayed using different thread
        /// </summary>
        public void StoreDB()
        {
            this.stored = false;
        }

        public void CreateSnapshot(Channel chan, string name)
        {
            try
            {
                if (!IsValid(name))
                {
                    IRC.DeliverMessage("This is not a valid name for snapshot, you can only use a-zA-Z and 0-9 chars", chan.Name);
                    return;
                }
                if (SnapshotManager != null)
                {
                    if (SnapshotManager.ThreadState == ThreadState.Running)
                    {
                        IRC.DeliverMessage("There is already another snapshot operation running for this channel", chan.Name);
                        return;
                    }
                }
                string datafile = InfobotModule.SnapshotsDirectory + Path.DirectorySeparatorChar + pChannel.Name + Path.DirectorySeparatorChar + name;
                if (File.Exists(datafile))
                {
                    IRC.DeliverMessage("The requested snapshot " + name + " already exist", chan.Name, libirc.Defs.Priority.Low);
                    return;
                }
                SnapshotManager = new Thread(SnapshotStart);
                temporaryData = datafile;
                SnapshotManager.Name = "Snapshot";
                SnapshotManager.Start();
            }
            catch (Exception fail)
            {
                Parent.HandleException(fail);
            }
        }

        /// <summary>
        /// Alias
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="al">Alias</param>
        /// <param name="user">User</param>
        /// <param name="chan"></param>
        /// <param name="enforced"></param>
        private void aliasKey(string key, string al, string user, Channel chan, bool enforced = false)
        {
            lock (this)
            {
                foreach (InfobotAlias stakey in Aliases)
                {
                    if (stakey.Name == al)
                    {
                        if (!chan.SuppressWarnings)
                        {
                            IRC.DeliverMessage(Localization.Localize("infobot7", chan.Language), chan.Name);
                        }
                        return;
                    }
                }
                if (!KeyExists(key))
                {
                    if (!enforced)
                    {
                        if (AliasExists(key))
                        {
                            IRC.DeliverMessage("Unable to create alias for " + key + " because the target is alias, but not a key, if you really want to create this broken alias do !" + al + " force-alias " + key, chan.Name);
                            return;
                        }
                        IRC.DeliverMessage("Unable to create alias for " + key + " because there is no such key, if you really want to create this broken alias do !" + al + " force-alias " + key, chan.Name);
                        return;
                    }
                }
                Aliases.Add(new InfobotAlias(al, key));
            }
            IRC.DeliverMessage(Localization.Localize("infobot8", chan.Language), chan.Name);
            this.StoreDB();
        }

        private void replaceKey(string Text, string key, string user, Channel chan)
        {
            lock (this)
            {
                try
                {
                    bool newkey = !KeyExists(key, Sensitive);
                    if (!newkey)
                        this.DeleteKey(key);
                    Keys.Add(new InfobotKey(key, Text, user, "false"));
                    if (newkey)
                        IRC.DeliverMessage(Localization.Localize("infobot6", chan.Language), chan);
                    else
                        IRC.DeliverMessage("Successfully replaced " + key, chan);
                    this.StoreDB();
                }
                catch (Exception b)
                {
                    Core.HandleException(b, "infobot");
                }
            }
        }

        private bool DeleteKey(string key)
        {
            lock (this)
            {
                foreach (InfobotKey keys in Keys)
                {
                    if (Sensitive)
                    {
                        if (keys.Key == key)
                        {
                            Keys.Remove(keys);
                            this.StoreDB();
                            return true;
                        }
                    }
                    else
                    {
                        if (keys.Key.ToLower() == key.ToLower())
                        {
                            Keys.Remove(keys);
                            this.StoreDB();
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void rmKey(string key, string user, Channel _ch)
        {
            if (this.DeleteKey(key))
                IRC.DeliverMessage(Localization.Localize("infobot9", _ch.Language) + key, _ch.Name);
            else
                IRC.DeliverMessage(Localization.Localize("infobot10", _ch.Language), _ch.Name);
        }
    }
}
