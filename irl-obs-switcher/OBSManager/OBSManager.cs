using ConsoleLogger;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IRLOBSSwitcher
{
    /// <summary>
    /// This is the class which manages the WebSocket connection with OBS and can be instructed to switch scenes
    /// in reaction to connect / disconnect events on any proxy
    /// </summary>
    public class OBSManager
    {
        private String OBS_Host;
        private ushort OBS_Port = 4444;
        private String OBS_URL;
        private String OBS_Password;
        private String OBS_SceneOnConnect;
        private String OBS_SceneOnDisconnect;
        private String? SemaphoreFileWhenConnected;
        protected OBSWebsocket obs;
        private String currentOBSScene = "";
        private OutputState currentOBSStreamState = OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED;
        public bool CurrentlyConnected { get; private set; } = false;

        public OBSManager(OBSWebSocketConnection? OBSWebSocketConnectionConfiguration)
        {
            if (OBSWebSocketConnectionConfiguration == null)
                throw new Exception("Please provide a proper OBS Websocket Configuration");
            
            // null checks and default values
            OBS_Host = string.IsNullOrEmpty(OBSWebSocketConnectionConfiguration.OBSWebSocketHost) ? "localhost" : OBSWebSocketConnectionConfiguration.OBSWebSocketHost;
            OBS_Port = OBSWebSocketConnectionConfiguration.OBSWebSocketPort.GetValueOrDefault(4444);
            OBS_URL = "ws://" + OBS_Host + ":" + OBS_Port.ToString();
            OBS_Password = string.IsNullOrEmpty(OBSWebSocketConnectionConfiguration.OBSWebSocketPassword) ? "" : OBSWebSocketConnectionConfiguration.OBSWebSocketPassword;
            SemaphoreFileWhenConnected = OBSWebSocketConnectionConfiguration.SemaphoreFileWhenConnected;
            OBS_SceneOnConnect = string.IsNullOrEmpty(OBSWebSocketConnectionConfiguration.OBSsceneOnConnect) ? throw new Exception("Please define a proper OBS Scene to be used when connected successfully.") : OBSWebSocketConnectionConfiguration.OBSsceneOnConnect;
            OBS_SceneOnDisconnect = string.IsNullOrEmpty(OBSWebSocketConnectionConfiguration.OBSSceneOnDisconnect) ? throw new Exception("Please define a proper OBS Scene to be used when connected successfully.") : OBSWebSocketConnectionConfiguration.OBSSceneOnDisconnect;

            obs = new OBSWebsocket();

            obs.Connected += onConnect;
            obs.Disconnected += onDisconnect;
            obs.CurrentProgramSceneChanged += onCurrentProgramSceneChanged;
            obs.CurrentPreviewSceneChanged += onCurrentProgramSceneChanged;
            obs.CurrentSceneCollectionChanged += onCurrentProgramSceneChanged;
            obs.StreamStateChanged += onStreamingStateChange;
            //obs. += onStreamData;

            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;

                while (true)
                {
                    if (!obs.IsConnected)
                    {
                        try
                        {
                            ConsoleLog.WriteLine("Connecting to OBS WebSocket on " + OBS_URL);
                            obs.Connect(OBS_URL, OBS_Password);
                        }
                        catch (AuthFailureException)
                        {
                            ConsoleLog.WriteLine("OBS WebSocket Authentication failed - Exiting.");
                            break;
                        }
                        catch (ErrorResponseException ex)
                        {
                            ConsoleLog.WriteLine("OBS WebSocket Connect failed: " + ex.Message);

                            ConsoleLog.WriteLine("Retrying to connect to OBS WebSocket...");
                            Thread.Sleep(2000);
                        }

                    } else
                    {
                        // connection is still there!
                        var streamStats = obs.GetStreamStatus();
                        if (streamStats.IsActive)
                            ConsoleLog.WriteLine("Total Stream Time: " + TimeSpan.FromMilliseconds(streamStats.Duration).ToString(@"hh\:mm\:ss\:fff"));
                    }
                    // only retry per timeout intervall
                    Thread.Sleep(500);
                }
            }).Start();

        }


        #region OBS WebSocket Eventhandling
        private void onConnect(object sender, EventArgs e)
        {
            var currentScene = obs.GetCurrentProgramScene();
            if (currentScene != null)
            {
                currentOBSScene = currentScene.Name;
            }

            var streamStatus = obs.GetStreamStatus();
            // initially set OBS stream status
            if (streamStatus.IsActive)
                onStreamingStateChange(obs, OutputState.OBS_WEBSOCKET_OUTPUT_STARTED);
            else
                onStreamingStateChange(obs, OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED);

            ConsoleLog.WriteLine("OBS WebSocket connection established.");
        }

        private void onDisconnect(object sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo e)
        {
            ConsoleLog.WriteLine("OBS WebSocket connection lost.");
        }

        private void onCurrentProgramSceneChanged(OBSWebsocket sender, string newSceneName)
        {
            ConsoleLog.WriteLine("OBS scene changed to "+ newSceneName);
            currentOBSScene = newSceneName;
        }

        private void onStreamingStateChange(OBSWebsocket sender, OutputStateChanged newState)
        {
            onStreamingStateChange(sender, newState.State);
        }

        private void onStreamingStateChange(OBSWebsocket sender, OutputState newState)
        {
            currentOBSStreamState = newState;

            switch (newState)
            {
                case OutputState.OBS_WEBSOCKET_OUTPUT_STARTING:
                    ConsoleLog.WriteLine("Public Stream starting...");
                    break;

                case OutputState.OBS_WEBSOCKET_OUTPUT_STARTED:
                    ConsoleLog.WriteLine("Public Stream started.");
                    break;

                case OutputState.OBS_WEBSOCKET_OUTPUT_STOPPING:
                    ConsoleLog.WriteLine("Public Stream stopping...");
                    break;

                case OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED:
                    ConsoleLog.WriteLine("Public Stream stopped.");
                    break;

                default:
                    ConsoleLog.WriteLine("Public Stream State unknown");
                    break;
            }
        }

        //private void onStreamData(OBSWebsocket sender, StreamStatus data)
        //{
        //    // TODO: we are getting data here - do something with it
        //    // data.TotalStreamTime...
        //    ConsoleLog.WriteLine("Total Stream Time: " + TimeSpan.FromSeconds(data.TotalStreamTime).ToString(@"hh\:mm\:ss\:fff"));
        //}
        #endregion

        #region Connection state changes of proxied connections
        /// <summary>
        /// this method is called when the connection is established successfully
        /// </summary>
        public void Connect()
        {
            // we only need to act when there's not already a connection...
            if (!CurrentlyConnected)
            {
                #region Handle Semaphore
                if (SemaphoreFileWhenConnected != null)
                {
                    File.WriteAllText(SemaphoreFileWhenConnected, OBS_SceneOnConnect);
                }
                #endregion

                #region Set OBS Scene
                obs.SetCurrentProgramScene(OBS_SceneOnConnect);
                #endregion

                // set connection status
                CurrentlyConnected = true;
            }
        }

        /// <summary>
        /// this method is called when the connection is lost
        /// </summary>
        public void Disconnect()
        {
            if (CurrentlyConnected)
            {
                #region Handle Semaphore
                if (SemaphoreFileWhenConnected != null)
                {
                    // delete the file when it exists...
                    if (File.Exists(SemaphoreFileWhenConnected))
                        File.Delete(SemaphoreFileWhenConnected);    
                }
                #endregion

                #region Set OBS Scene
                obs.SetCurrentProgramScene(OBS_SceneOnDisconnect);
                #endregion

                CurrentlyConnected = false;
            }
        }
        #endregion

    }
}
