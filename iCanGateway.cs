using industREAL.CAN.CORE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace industREAL.CAN.CANGW
{
    public interface iCanGateway
    {
        #region CONNECTION
        /// <summary>
        /// Serial over USB based connection;
        /// </summary>
        /// <param name="serialPort"></param>
        /// <param name="baudRate"></param>
        /// <returns></returns>
        public bool Connect(string serialPort, uint baudRate);

        /// <summary>
        /// IP Based connection.
        /// </summary>
        /// <param name="serverIp"></param>
        /// <param name="serverPort"></param>
        /// <returns></returns>
        public bool Connect(string serverIp, int serverPort);
        
        /// <summary>
        /// Disconnects, if connection active. 
        /// </summary>
        public void Disconnect();

        #endregion CONNECTION

        #region CONFIGURATION
        public bool Configure(CanInterface channel, uint canBaudRate, bool isFD,bool isBRS);

        public bool EnableLog(string logName);

        public void DisableLog();

        public string GetHardwareVersion();

        public string GetFirmwareVersion();

        #endregion CONFIGURATION

        #region SEND/RECEIVE
        /// <summary>
        /// Send a CAN Frame immediately
        /// </summary>
        /// <param name="outgoingFrame"></param>
        public void SendSingleCanFrame(CanInterface channel, CanFrame outgoingFrame);

        /// <summary>
        /// Adds the frame to the outgoing queue. It sends automatically after flushTimeout expired, or
        /// manually called @FlushMultipleFrames();
        /// </summary>
        /// <param name="outFrame"></param>
        public void AddFrameToQueue(CanInterface channel, CanFrame outFrame, uint flushTimeout);

        public void FlushMultipleFrames();

        #endregion SEND/RECEIVE

        #region EVENTS
        public delegate void CanFrameReceived(CanFrame receivedFrame);

        public event CanFrameReceived? FrameReceived;
        #endregion EVENTS
    }
}
