﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using Lidgren.Network;
using System.Threading;

using ICities;
using UnityEngine;
using ColossalFramework;

namespace Gurkenplayer
{
    public delegate void ClientEventHandler(object sender, EventArgs e);
    public class Client : IDisposable
    {
        public event ClientEventHandler clientConnectedEvent;
        public event ClientEventHandler clientDisconnectedEvent;

        public virtual void OnClientConnected(EventArgs e)
        {
            if (clientConnectedEvent != null)
                clientConnectedEvent(this, e);
        }
        public virtual void OnClientDisconnected(EventArgs e)
        {
            if (clientDisconnectedEvent != null)
                clientDisconnectedEvent(this, e);
        }

        //Fields
        #region Fields
        static Client instance;
        NetPeerConfiguration config;
        NetClient client;
        string appIdentifier = "Gurkenplayer";
        string serverIP = "localhost";
        int serverPort = 4420;
        string serverPassword = "Password";
        static bool isClientConnected = false;
        static bool stopMessageProcessingThread = false;
        bool disposed = false;
        string username = "usr";
        ParameterizedThreadStart pts;
        Thread messageProcessingThread;
        #endregion

        //Properties
        #region props
        /// <summary>
        /// Returns the IP-Address of the server.
        /// </summary>
        public string ServerIP
        {
            get { return serverIP; }
            set { serverIP = value; }
        }

        /// <summary>
        /// Returns the used server password.
        /// </summary>
        public string ServerPassword
        {
            get { return serverPassword; }
            set { serverPassword = value; }
        }

        /// <summary>
        /// Returns the used server port.
        /// </summary>
        public int ServerPort
        {
            get { return serverPort; }
            set { serverPort = value; }
        }

        /// <summary>
        /// Indicates if the client is connected to a server. Is set to "true" inside the StatusChanged of ProcessMessage.
        /// </summary>
        public static bool IsClientConnected
        {
            get 
            {
                if (Instance.client.ConnectionStatus == NetConnectionStatus.Connected)
                    return true; //Check first if the ConnectionStatus is set to connected
                else if (isClientConnected)
                    return true; //If not check if the bool isClientConnected is set to true
                else
                    return false; //Otherwise it is not connected
            }
            set { Client.isClientConnected = value; }
        }

        /// <summary>
        /// Returns true if the client is initialized (instance != null).
        /// </summary>
        public static bool IsClientInitialized
        {
            get 
            {
                if (instance != null)
                    return true;
                
                return false;
            }
        }

        /// <summary>
        /// Returns the username of the client.
        /// </summary>
        public string Username
        {
            get { return username; }
            set { username = value; }
        }

        /// <summary>
        /// Returns true when the client is initialized and connected to a server.
        /// </summary>
        public bool CanSendMessage
        {
            get
            {
                if (IsClientConnected)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Returns the current NetConnectionStatus of the client.
        /// </summary>
        public NetConnectionStatus ClientConnectionStatus
        {
            get { return client.ConnectionStatus; }
        }

        /// <summary>
        /// Indicates if the message processing thread of the client should be stopped gracefully.
        /// </summary>
        public static bool StopMessageProcessingThread
        {
            get { return Client.stopMessageProcessingThread; }
            set { Client.stopMessageProcessingThread = value; }
        }
        #endregion

        //Singleton pattern stuff
        /// <summary>
        /// Returns the current instance of the client and creates a new one if it is null.
        /// </summary>
        public static Client Instance
        {
            get
            {
                //Returns the instance if it is not null. If it is, return new instance
                return instance ?? (instance = new Client()); //Test

                //if (instance == null)
                //    instance = new Client();

                //return instance;
            }
        }

        //Constructor
        /// <summary>
        /// Private Client constructor for the singleton pattern.
        /// </summary>
        private Client()
        {
            config = new NetPeerConfiguration(appIdentifier);
            config.MaximumHandshakeAttempts = 1;
            config.ResendHandshakeInterval = 1;
            config.AutoFlushSendQueue = false; //client.SendMessage(message, NetDeliveryMethod); is needed for sending
            client = new NetClient(config);
            pts = new ParameterizedThreadStart(this.ProcessMessage);
            GurkenplayerMod.MPRole = MPRoleType.Client;
        }

        /// <summary>
        /// Destructor logic.
        /// </summary>
        ~Client()
        {
            Dispose();
        }

        //Methods
        /// <summary>
        /// Method with optional parameters which is used to connect to an existing server.
        /// Empty arguments take the default value.
        /// </summary>
        /// <param name="ip">The server ip to connect to. Default: localhost</param>
        /// <param name="port">The server port which is used to connect. Default: 4230</param>
        /// <param name="password">The server password which is used to connect. Default: none</param>
        public void ConnectToServer(string ip = "localhost", int port = 4230, string password = "")
        {
            Log.Message("Client connecting to server. if(IsClientConnected) -> Disconnect...(): " + IsClientConnected + ". IsClientConnected: " + IsClientConnected + " Current MPRole: " + GurkenplayerMod.MPRole);
            DisconnectFromServer();

            //Throw Exception when ip is not valid
            if (ip != "localhost" && ip.Split('.').Length != 4)
                throw new MPException("Invalid server ip address. Check it please.");

            Log.Warning(String.Format("Client trying to connect to ip:{0} port:{1} password:{2} maxretry:{3} retryinterval:{4} MPRole:{5}", ip, port, password, client.Configuration.MaximumHandshakeAttempts, client.Configuration.ResendHandshakeInterval, GurkenplayerMod.MPRole));

            //Manipulating fields
            ServerIP = ip;
            ServerPort = port;
            ServerPassword = password;

            //Write approval message with password
            NetOutgoingMessage approvalMessage = client.CreateMessage();  //Approval message with password
            approvalMessage.Write(ServerPassword);
            approvalMessage.Write(Username);
            client.Start();
            Log.Message("Client started. Trying to connect.");
            client.Connect(ServerIP, ServerPort, approvalMessage);
            Log.Message("after client.Connect. Starting Thread now.");

            IsClientConnected = true;
            StopMessageProcessingThread = false;

            //Separate thread in which the received messages are handled
            messageProcessingThread = new Thread(pts);
            messageProcessingThread.Start(client);

            //Raise event if available
            OnClientConnected(new EventArgs());
            Log.Message("Client should be connected. Current MPRole: " + GurkenplayerMod.MPRole + " MessageProcessingThread alive? " + messageProcessingThread.IsAlive);
        }

        /// <summary>
        /// Disconnects the client from the server
        /// </summary>
        public void DisconnectFromServer()
        {
            if (!IsClientConnected)
                return; //If client is not  connected, return

            Log.Message("Disconnecting from the server. Current MPRole: " + GurkenplayerMod.MPRole);

            try
            {
                client.Disconnect("Bye Bye Client.");
            }
            catch (Exception ex)
            {
                Log.Error("Exception while disconnecting client. ex: " + ex.ToString());
            }
            finally
            {
                //Reconfiguration
                config = new NetPeerConfiguration(appIdentifier);
                config.MaximumHandshakeAttempts = 1;
                config.ResendHandshakeInterval = 1;
                config.AutoFlushSendQueue = false; //client.SendMessage(message, NetDeliveryMethod); is needed for sending
                client = new NetClient(config);

                IsClientConnected = false;
                if (messageProcessingThread.IsAlive)
                    StopMessageProcessingThread = true;

                OnClientDisconnected(new EventArgs());

                Log.Error("Disconnected. Is thread still alive? " + Instance.messageProcessingThread.IsAlive);
            }
        }

        /// <summary>
        /// Gets rid of the Client instance.
        /// </summary>
        public void Dispose()
        {
            try
            {
                Log.Message("Disposing client. Current MPRole: " + GurkenplayerMod.MPRole);
                Instance.DisconnectFromServer();
                if (!IsClientConnected)
                    GurkenplayerMod.MPRole = MPRoleType.None;
                
                Log.Message("Client disposed. Current MPRole: " + GurkenplayerMod.MPRole);
            }
            catch (Exception ex)
            {
                throw new MPException("Exception in Client.Dispose()", ex);
            }
            finally
            {
                if (!disposed)
                {
                    GC.SuppressFinalize(this);
                    disposed = true;
                }
            }
        }

        /// <summary>
        /// ProcessMessage runs in a separate thread and manages the received server messages.
        /// </summary>
        /// <param name="obj">object obj represents a NetClient object.</param>
        private void ProcessMessage(object obj)
        {
            try
            {
                Log.Warning("Entering ProcessMessage");
                NetClient client = (NetClient)obj;
                NetIncomingMessage msg;

                while (true)
                {
                    //Stop the thread if the MPRoleType is not Client or the bool StopMessageProcessingThread is true (default: false).
                    if (GurkenplayerMod.MPRole != MPRoleType.Client || StopMessageProcessingThread)
                        break;

                    while ((msg = client.ReadMessage()) != null)
                    {
                        switch (msg.MessageType)
                        {
                            //Zum debuggen
                            #region NetIncomingMessageType Debug
                            case NetIncomingMessageType.VerboseDebugMessage: //Debug
                            case NetIncomingMessageType.DebugMessage: //Debug
                            case NetIncomingMessageType.WarningMessage: //Debug
                            case NetIncomingMessageType.ErrorMessage: //Debug
                                Log.Warning("DebugMessage: " + msg.ReadString());
                                break;
                            #endregion

                            #region NetIncomingMessageType.StatusChanged
                            case NetIncomingMessageType.StatusChanged:
                                NetConnectionStatus state = (NetConnectionStatus)msg.ReadByte();
                                Log.Warning("ProcessMessage entry state: " + state);
                                if (state == NetConnectionStatus.Connected)
                                {
                                    IsClientConnected = true;
                                    OnClientConnected(new EventArgs());
                                    Log.Message("You connected. Client IP: " + msg.SenderEndPoint);
                                }
                                else if (state == NetConnectionStatus.Disconnected || state == NetConnectionStatus.Disconnecting || state == NetConnectionStatus.None)
                                {
                                    IsClientConnected = false;
                                    StopMessageProcessingThread = true;
                                    Log.Message("You disconnected. Client IP: " + msg.SenderEndPoint);
                                    OnClientDisconnected(new EventArgs());
                                    GurkenplayerMod.MPRole = MPRoleType.Resetting;
                                }
                                break;
                            #endregion

                            #region NetIncomingMessageType.Data
                            case NetIncomingMessageType.Data:
                                byte type = msg.ReadByte();
                                ProgressData((MPMessageType)type, msg); //Test
                                break;
                            #endregion

                            #region NetIncomingMessageType.ConnectionApproval
                            case NetIncomingMessageType.ConnectionApproval:
                                break;
                            #endregion

                            default:
                                Log.Warning("Client ProcessMessage: Unhandled type/message: " + msg.MessageType);
                                break;
                        }
                    }
                }
                StopMessageProcessingThread = false;
                Log.Warning("Leaving Client Message Progressing Loop");
            }
            catch (NetException ex)
            {
                throw new MPException("NetException (Lidgren) in Client.ProcessMessage. Message: " + ex.Message, ex);
            }
            catch (Exception ex)
            {
                throw new MPException("Exception in Client.ProcessMessage(). Message: " + ex.Message, ex);
            }

        }
        
        /// <summary>
        /// Method to process the received information.
        /// </summary>
        /// <param name="type">Type of the message. Indicates what the message's contents are.</param>
        /// <param name="msg">The message to process.</param>
        private void ProgressData(int type, NetIncomingMessage msg)
        {
            switch (type)
            {
                case 0x2000: //Receiving money
                    Log.Message("Client received 0x2000");
                    EcoExtBase._CurrentMoneyAmount = msg.ReadInt64();
                    EcoExtBase._InternalMoneyAmount = msg.ReadInt64();
                    break;
                case 0x3000: //Receiving demand
                    Log.Message("Client received 0x3000");
                    DemandExtBase._CommercialDemand = msg.ReadInt32();
                    DemandExtBase._ResidentalDemand = msg.ReadInt32();
                    DemandExtBase._WorkplaceDemand = msg.ReadInt32();
                    break;
                case 0x4000:
                    Log.Message("Client received 0x4000");
                    AreaExtBase._XCoordinate= msg.ReadInt32();
                    AreaExtBase._ZCoordinate = msg.ReadInt32();
                    //INFO: The unlock process is activated once every 4 seconds simutaniously with the
                    //EcoExtBase.OnUpdateMoneyAmount(long internalMoneyAmount).
                    //Maybe I find a direct way to unlock a tile within AreaExtBase
                    break;
                default: //Unbehandelte ID
                    Log.Warning("Client ProgressData: Unhandled ID/type: " + type);
                    break;
            }
        }
        private void ProgressData(MPMessageType msgType, NetIncomingMessage msg)
        {
            switch (msgType)
            {
                case MPMessageType.MoneyUpdate: //Receiving money
                    Log.Message("Client received " + msgType);
                    EcoExtBase._CurrentMoneyAmount = msg.ReadInt64();
                    EcoExtBase._InternalMoneyAmount = msg.ReadInt64();
                    break;
                case MPMessageType.DemandUpdate: //Receiving demand
                    Log.Message("Client received " + msgType);
                    DemandExtBase._CommercialDemand = msg.ReadInt32();
                    DemandExtBase._ResidentalDemand = msg.ReadInt32();
                    DemandExtBase._WorkplaceDemand = msg.ReadInt32();
                    break;
                case MPMessageType.TileUpdate:
                    Log.Message("Client received " + msgType);
                    AreaExtBase._XCoordinate = msg.ReadInt32();
                    AreaExtBase._ZCoordinate = msg.ReadInt32();
                    //INFO: The unlock process is activated once every 4 seconds simutaniously with the
                    //EcoExtBase.OnUpdateMoneyAmount(long internalMoneyAmount).
                    //Maybe I find a direct way to unlock a tile within AreaExtBase
                    break;
                default: //Unbehandelte ID
                    Log.Warning("Client ProgressData: Unhandled ID/type: " + msgType);
                    break;
            }
        }

        /// <summary>
        /// Send the EconomyInformation of the client to the server to synchronize.
        /// </summary>
        public void SendEconomyInformationUpdateToServer()
        {
            if (CanSendMessage)
            {
                NetOutgoingMessage msg = client.CreateMessage((byte)MPMessageType.MoneyUpdate);
                msg.Write(EconomyManager.instance.LastCashAmount);//EcoExtBase._CurrentMoneyAmount
                msg.Write(EconomyManager.instance.InternalCashAmount);//EcoExtBase._InternalMoneyAmount
                client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered);
                client.FlushSendQueue();
            }
        }

        /// <summary>
        /// Sends the DemandInformation of the client to the server to synchronize.
        /// </summary>
        public void SendDemandInformationUpdateToServer()
        {
            if (CanSendMessage)
            {
                NetOutgoingMessage msg = client.CreateMessage((byte)MPMessageType.DemandUpdate);
                msg.Write(DemandExtBase._CommercialDemand);
                msg.Write(DemandExtBase._ResidentalDemand);
                msg.Write(DemandExtBase._WorkplaceDemand);
                client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered);
                client.FlushSendQueue();
            }
        }

        /// <summary>
        /// Sends a message to the server indicating which tile shall be unlocked.
        /// </summary>
        /// <param name="x">X coordinate of the tile.</param>
        /// <param name="z">Z coordinate of the tile.</param>
        public void SendAreaInformationUpdateToServer(int x, int z)
        {
            if (CanSendMessage)
            {
                NetOutgoingMessage msg = client.CreateMessage((byte)MPMessageType.TileUpdate);
                msg.Write(x);
                msg.Write(z);
                client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered);
                client.FlushSendQueue();
            }
        }
    }
}
