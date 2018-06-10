using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;


namespace embytogether
{
    
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogManager logManager, ISessionManager sessionManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            Logger = logManager.GetLogger(GetType().Name);
            _sessionManager = sessionManager;


            _sessionManager.SessionStarted += _sessionManager_SessionStarted;
            _sessionManager.SessionEnded += _sessionManager_SessionEnded;
            _sessionManager.PlaybackStart += _sessionManager_PlaybackStart;
            _sessionManager.PlaybackStopped += _sessionManager_PlaybackStopped;
            _sessionManager.PlaybackProgress += _sessionManager_PlaybackProgress;
            //_sessionManager.CapabilitiesChanged += _sessionManager_CapabilitiesChanged;
            _sessionManager.SessionActivity += _sessionManager_SessionActivity;

            // remember state (play/paused) for each currently streaming content by guid
            content_state = new Dictionary<SessionDescriptor, TogetherSession>();

        }

        public static string PluginName = "EmbyTogether";
        private static ILogger Logger;
        private readonly ISessionManager _sessionManager;
        private readonly Guid _id = new Guid("A45ECC1E-4301-4DC2-B515-173EAEF32B49");

        public override Guid Id
        {
            get { return _id; }
        }


        /// <summary>
        /// Gets the name of the plugin
        /// </summary>
        /// <value>The name.</value>
        public override string Name
        {
            get { return PluginName; }
        }


        /// <summary>
        /// Gets the description.
        /// </summary>
        /// <value>The description.</value>
        public override string Description
        {
            get
            {
                return "Watch Content Together!";
            }
        }


        /// <summary>
        /// Gets the instance.
        /// </summary>
        /// <value>The instance.</value>
        public static Plugin Instance { get; private set; }


        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "EmbyTogether",
                    EmbeddedResourcePath = GetType().Namespace + ".config.html"
                }
            };
        }


        //Merge user and content guid and treat that as a session
        private struct SessionDescriptor
        {
            public Guid user;
            public Guid content;

            public SessionDescriptor(Guid user, Guid content) : this()
            {
                this.content = content;
                this.user = user;
            }
        }
        
        private class TogetherSession
        {
            public enum State { paused, playing };

            public State state;
            public DateTime last_update;
            public Guid? last_commander_uid;
            public decimal? ticks;
            
            public TogetherSession(State state, DateTime? last_update = null)
            {
                this.state = state;
                this.last_update = last_update ?? DateTime.Now;
            }
        }
        private Dictionary<SessionDescriptor, TogetherSession> content_state;


        public static void DebugLogger(string message, params object[] paramList)
        {
            //if (Instance.Configuration.EnableDebugLogging)
            Logger.Info($"[EmbyTogether] {message}", paramList);
        }
        

        private void send_seek_command(long? ticks, string ContentId, Guid? userId)
        {
            Plugin.DebugLogger($"Seeking to {ticks}");

            PlaystateRequest command = new PlaystateRequest
            {
                Command = PlaystateCommand.Seek,
                SeekPositionTicks = ticks,
                ControllingUserId = userId.HasValue ? userId.Value.ToString("N") : null
            };
            _sessionManager.SendPlaystateCommand(ContentId, ContentId, command, CancellationToken.None);
        }


        private void send_command(PlaystateCommand type, string ContentId, Guid? userId)
        {
            PlaystateRequest command = new PlaystateRequest
            {
                Command = type,
                ControllingUserId = userId.HasValue ? userId.Value.ToString("N") : null
            };
            _sessionManager.SendPlaystateCommand(ContentId, ContentId, command, CancellationToken.None);
        }


        void _sessionManager_SessionStarted(object sender, SessionEventArgs e)
        {
            if(!Plugin.Instance.Configuration.SelectedGUIDs.Contains(e.SessionInfo.UserId.HasValue ? e.SessionInfo.UserId.Value.ToString("N") : null))
            {
                return;
            }
            Plugin.DebugLogger("Session manager SessionStarted!");
        }


        void _sessionManager_SessionEnded(object sender, SessionEventArgs e)
        {
            if (!Plugin.Instance.Configuration.SelectedGUIDs.Contains(e.SessionInfo.UserId.HasValue ? e.SessionInfo.UserId.Value.ToString("N") : null))
            {
                return;
            }
            Plugin.DebugLogger("Session manager SessionEnded!");
        }


        void _sessionManager_PlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            if (!Plugin.Instance.Configuration.SelectedGUIDs.Contains(e.Session.UserId.HasValue ? e.Session.UserId.Value.ToString("N") : null))
            {
                return;
            }
            Plugin.DebugLogger("Session manager PlaybackStart!");
            SessionDescriptor descr = new SessionDescriptor((Guid)e.Session.UserId,
                                              e.Session.FullNowPlayingItem.Id);
            // First one to play
            if (!content_state.ContainsKey(descr)) {
                content_state[descr] = new TogetherSession(TogetherSession.State.playing)
                {
                    ticks = e.PlaybackPositionTicks
                };
                Plugin.DebugLogger("Created new EmbyTogether Session!");
            }
            // Someone else is already playing, seek to their location
            else
            {
                Plugin.DebugLogger("New Client joining to established Session!");
                // Lets get the first matching session with matching userid and content id
                var sessions = _sessionManager.Sessions.Where(
                    s => e.Session.UserId == s.UserId &&
                    e.Session.FullNowPlayingItem.Id == s.FullNowPlayingItem.Id &&
                    s.Id != e.Session.Id &&    // force different session, otherwise we might pick up ourselves
                    s.IsActive
                  );

                if (sessions.Count() > 0)
                {
                    // Get ticks from this session and seek new user there
                    var session = sessions.First();

                    long last_update = session.LastActivityDate.Ticks;
                    long current = DateTime.Now.Ticks;
                    Plugin.DebugLogger($"Difference in ticks: {last_update} {current} {current - last_update}!");
                    send_seek_command(session.PlayState.PositionTicks, e.Session.Id, e.Session.UserId);

                    // Is the current state paused? If yes, pause new user
                    if (content_state[descr].state == TogetherSession.State.paused)
                    {
                        send_command(PlaystateCommand.Pause, e.Session.Id, e.Session.UserId);
                    }
                }
                else
                {
                    Plugin.DebugLogger("Session exists, but without users. This should never happen!");
                }
            }
        }


        void _sessionManager_PlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            if (!Plugin.Instance.Configuration.SelectedGUIDs.Contains(e.Session.UserId.HasValue ? e.Session.UserId.Value.ToString("N") : null))
            {
                return;
            }
            Plugin.DebugLogger("Session manager PlaybackStopped!");

            //Remove state from dict if last client stopped playing..
            SessionDescriptor descr = new SessionDescriptor((Guid)e.Session.UserId,
                                              e.Session.FullNowPlayingItem.Id);
            if (content_state.ContainsKey(descr))
            {
                int usercount = _sessionManager.Sessions.Where(
                            s => e.Session.UserId == s.UserId &&
                            e.Session.FullNowPlayingItem.Id == s.FullNowPlayingItem.Id &&
                            s.IsActive &&
                            s.Id != e.Session.Id
                       ).Count();

                if (usercount == 0)
                {
                    content_state.Remove(descr);
                    Plugin.DebugLogger($"Removed Session form Dict. Remainig Sessions: {content_state.Count}");
                }
                else
                {
                    Plugin.DebugLogger($"Other Clients ({usercount}) still active on this session! Remainig Sessions: {content_state.Count}");
                }
            }
        }


        void _sessionManager_PlaybackProgress(object sender, PlaybackProgressEventArgs e)
        {
            if (!Plugin.Instance.Configuration.SelectedGUIDs.Contains(e.Session.UserId.HasValue ? e.Session.UserId.Value.ToString("N") : null))
            {
                return;
            }
            //Plugin.DebugLogger($"Session manager PlaybackProgress! {e.PlaybackPositionTicks}, {e.IsPaused}, {e.Session.UserId.Value.ToString("N")}, {e.Session.FullNowPlayingItem.Id}");
            Plugin.DebugLogger($"{e.Session.Id}: Session manager PlaybackProgress::! {e.PlaybackPositionTicks}, {e.IsPaused}");


            SessionDescriptor descr = new SessionDescriptor((Guid) e.Session.UserId,
                                              e.Session.FullNowPlayingItem.Id);

            // Playback Progress might get triggered before playback started --> key might not exist yet
            if (!content_state.ContainsKey(descr))
            {
                Plugin.DebugLogger($"{e.Session.Id}: Key not Found!");
                return;
            }
            TogetherSession ts = content_state[descr];

            // only check play/pause changes at most once per 5 seconds, to avoid feedback loops (reduced to 1, if same user issued last command)
            Plugin.DebugLogger($"{e.Session.Id}: Time from last command: {(DateTime.Now - ts.last_update).TotalSeconds}");
            if (((DateTime.Now - ts.last_update).TotalSeconds > Plugin.Instance.Configuration.Timeoffset_cmd_same_user && ts.last_commander_uid == e.Session.UserId) ||
                ((DateTime.Now - ts.last_update).TotalSeconds > Plugin.Instance.Configuration.Timeoffset_cmd_diff_user))
            {
                List<PlaystateRequest> commands = ProcessClientstate(e, ts);

                int num = 0;
                string targets = $"{e.Session.Id}: Send to: ";
                //only send commands to sessions of the same user which play the same content
                foreach (SessionInfo s in _sessionManager.Sessions.Where( s => 
                    e.Session.UserId == s.UserId &&
                    e.Session.FullNowPlayingItem.Id == s.FullNowPlayingItem.Id &&
                    s.Id != e.Session.Id))
                {
                    // Plugin.DebugLogger($"{e.Session.Id}: Sending command to #{num} {s.UserId.Value.ToString("N")}, {s.FullNowPlayingItem.Id}");
                    num++;
                    foreach (var command in commands)
                    {
                        _sessionManager.SendPlaystateCommand(s.Id, s.Id, command, CancellationToken.None);
                    }
                    targets += $"{s.Id}; ";
                }
                Plugin.DebugLogger(targets);
            }
            else
            {
                Plugin.DebugLogger($"{e.Session.Id}: Not scanning package for commands, since it arrived too soon!");
            }
        }


        private static List<PlaystateRequest> ProcessClientstate(PlaybackProgressEventArgs e, TogetherSession ts)
        {
            List<PlaystateRequest> commands = new List<PlaystateRequest>();

            //Check for seeking [no command send, just different time)
            // Plugin.DebugLogger($"{e.Session.Id}: Seeking chk! {Math.Abs(ts.ticks - e.PlaybackPositionTicks ?? 0)}, {ts.ticks}, {e.PlaybackPositionTicks}, {e.IsPaused}");
            if (Math.Abs((ts.ticks - e.PlaybackPositionTicks) ?? 0) > Plugin.Instance.Configuration.Timeoffset_seek * 10000000) // 10000000 == 1 second
            {
                //Assume a user has seeked. --> seek with all users
                Plugin.DebugLogger($"{e.Session.Id}: Seeking all clients! {Math.Abs(ts.ticks - e.PlaybackPositionTicks ?? 0)} {e.PlaybackPositionTicks}, {e.IsPaused}");
                commands.Add(new PlaystateRequest
                {
                    Command = PlaystateCommand.Seek,
                    SeekPositionTicks = e.PlaybackPositionTicks,
                    ControllingUserId = e.Session.UserId.HasValue ? e.Session.UserId.Value.ToString("N") : null
                });

            }
            ts.ticks = e.PlaybackPositionTicks; // TODO: This will jump around with multiple clients. implement some kind of averaging?

            //if stream paused, but state is playing, pause all clients
            if (e.IsPaused && ts.state == TogetherSession.State.playing)
            {
                //pause all clients
                Plugin.DebugLogger($"{e.Session.Id}: Pausing all clients! {e.PlaybackPositionTicks}, {e.IsPaused}");
                commands.Add(new PlaystateRequest
                {
                    Command = PlaystateCommand.Pause,
                    ControllingUserId = e.Session.UserId.HasValue ? e.Session.UserId.Value.ToString("N") : null
                });
                ts.state = TogetherSession.State.paused;
                ts.last_update = DateTime.Now;
                ts.last_commander_uid = e.Session.UserId;
            }
            else if (!e.IsPaused && ts.state == TogetherSession.State.paused)
            {
                // Seek all clients to the time of the unpausing user
                Plugin.DebugLogger($"{e.Session.Id}: Unpausing all clients! {e.PlaybackPositionTicks}, {e.IsPaused}");
                commands.Add(new PlaystateRequest
                {
                    Command = PlaystateCommand.Seek,
                    SeekPositionTicks = e.PlaybackPositionTicks,
                    ControllingUserId = e.Session.UserId.HasValue ? e.Session.UserId.Value.ToString("N") : null
                });
                // Unpause all clients
                commands.Add(new PlaystateRequest
                {
                    Command = PlaystateCommand.Unpause,
                    ControllingUserId = e.Session.UserId.HasValue ? e.Session.UserId.Value.ToString("N") : null
                });
                ts.state = TogetherSession.State.playing;
                ts.last_update = DateTime.Now;
                ts.last_commander_uid = e.Session.UserId;
            }
            return commands;
        }


        void _sessionManager_SessionActivity(object sender, SessionEventArgs e)
        {
            if (!Plugin.Instance.Configuration.SelectedGUIDs.Contains(e.SessionInfo.UserId.HasValue ? e.SessionInfo.UserId.Value.ToString("N") : null))
            {
                return;
            }
            Plugin.DebugLogger("Session manager SessionActivity!");
        }


        void IDisposable.Dispose()
        {
            _sessionManager.SessionStarted -= _sessionManager_SessionStarted;
            _sessionManager.SessionEnded -= _sessionManager_SessionEnded;
            _sessionManager.PlaybackStart -= _sessionManager_PlaybackStart;
            _sessionManager.PlaybackStopped -= _sessionManager_PlaybackStopped;
            _sessionManager.PlaybackProgress -= _sessionManager_PlaybackProgress;
            //_sessionManager.CapabilitiesChanged -= _sessionManager_CapabilitiesChanged;
            _sessionManager.SessionActivity -= _sessionManager_SessionActivity;
            Plugin.DebugLogger("Plugin Disposed!");
        }
    }
}
