/* [MAJOR FEATURES TO-DO]
 * - Add firmware version, hardware version, firmware upgrade
 * - time sync.
 * - BUS management, like retry, BUS OFF, etc. Listen only.
 * - Backtracking sent messages by ID. track until UI. 
 * - USB support
 * - Bonjour 5353
 * - send multiple frames add timeout and flush frames automatically.
 * - multiple socket to be used separately (ch A, B. etc)
 *                     
 * 
 * 
 * 
 * [BUG]
 * I observed that the device is not responding after a while. 
 * Test and bugfix!
 * alma
 */

/* [RELEASE NOTES]
 * * <0.4.5>
 *      Removed common items to HCE.General
 * <0.4.3> * <0.4.3>
 *      Namespace rework to industREAL.CAN.CANGW
 * 
 * <0.4.2>
 *      Watchdog implemented, that checks HeartBeat messages
 * 
 * <0.4.1>
 *      Basics of Control messages added and tested
 *          HeartBeat, TerminationControl.
 * 
 * <0.3.1>
 *      Proper Disposing with IDisposable
 *           
 * 
 * <0.3.0>
 *      NuGet support
 * 
 * 
 */


using industREAL.CAN.CANGW.Network;
using industREAL.CAN.CANGW.Utils;
using industREAL.CAN.CANGW.Types;
using industREAL.CAN.CORE;

using System.Collections.Concurrent;
using System.Diagnostics;
using industREAL.CAN.CANGW;

namespace industREAL.CAN
{

    /* TRANSMIT message(es):
     * It sends an UDP package containing one/multiple CAN frames in the following format:
     * - [0] FUNCTION CODE : 's' or 'm': singleframe/multipleframe
     * - [1]   if FUNC_CODE is 'm' than it contains SIZE: number of tCanGwFrame structures
     * - [1..] if FUNC_CODE is 's' it contains tCanGwFrame structure continously
     * - [2..] n x tCanGwFrame stucture: n equals times SIZE
     * Than COBS encode and terminate with 0.
     * 
     * RECEIVE message(s):
     * [0] SOF ('#' for CAN RX)
     * [1] number of tCanGwFrame stuctures
     * [2..] tCanGwFrame structures continously. Be aware that tCanGwFrame data size could differ by each CAN frame. 
     * 
     * */

    public class CanGateway : IDisposable
    {
        #region LOCALS
        //AsyncUDPBroadcastClient     client;
        AsyncUDPClient              control;
        ConcurrentQueue <CanFrame>  frames;
        Watchdog                    watchdog;
        public bool                 isConnected { get; private set; }
        #endregion LOCALS

        #region EVENTS

        public delegate void CanFrameReceived(CanFrame receivedFrame);
        public delegate void GatewayStatusUpdate(byte TEC_A, byte REC_A, byte TEC_B, byte REC_B);
        public delegate void ConnectionChanged(bool isConnected);

        public event CanFrameReceived     FrameReceived;
        public event GatewayStatusUpdate? OnGatewayStatusUpdated;
        public event ConnectionChanged?   OnConnectionChanged;
        #endregion EVENTS

        #region CONSTUCTOR
        public CanGateway(string serverIp, int serverPort)
        {
            isConnected = false;
            frames      = new ConcurrentQueue<CanFrame>();
            //client      = new AsyncUDPBroadcastClient(serverIp, serverPort);
            control     = new AsyncUDPClient(serverIp, serverPort+1);
            watchdog    = new(TimeSpan.FromMilliseconds(800),true);

            //client.NotifyListeners += ControlNotifyListeners;//Client_NotifyListeners;
            control.NotifyListeners += Client_NotifyListeners;
            watchdog.TimedOut       += Watchdog_TimedOut;
        }

        public CanGateway(string pPort, uint pBaudrate)
        {
            throw new NotImplementedException();
        }
        #endregion CONSTUCTOR

        #region PRIVATE_METHODS
        private void ControlNotifyListeners(byte[] pData)
        {
            throw new NotImplementedException();
        }

        private void Watchdog_TimedOut()
        {
            if (isConnected)
            {
                isConnected= false;
                OnConnectionChanged?.Invoke(isConnected);
            }   
        }

        private void Client_NotifyListeners(byte[] receivedBytes)
        {
            try
            {
                if (receivedBytes == null)
                {
                    throw new ArgumentNullException("UDP Dispatcher: NULL pointer");
                }
                if (receivedBytes.Length <= 12)
                {
                    throw new ArgumentException("UDP data size is less than minimum");
                }

                byte[] receivedData = COBS.Decode(receivedBytes);

                if (receivedData[0] == '#')
                {
                    if (isConnected == false)
                    {
                        isConnected = true;
                        OnConnectionChanged?.Invoke(isConnected);
                    }

                    try
                    {
                        List<CanFrame> frames = FrameDeserializer.DeserializeFrameBuffer(receivedData);
                        foreach (var frame in frames)
                        {
                            if (frame._flags == CanFlags.CONTROL)
                            {
                                switch ((GwCommands)(frame._frameID & 0xFF))
                                {
                                    case GwCommands.CMD_GET_SERIAL_NUMBER:
                                        break;
                                    case GwCommands.CMD_GET_HARDWARE_VERSION:
                                        break;
                                    case GwCommands.CMD_GET_FIRMWARE_VERSION:
                                        break;
                                    case GwCommands.CMD_GETSET_TERMINATION:
                                        break;
                                    case GwCommands.CMD_SET_TO_BOOTSTRAP:
                                        break;
                                    case GwCommands.CMD_GETSET_IF_SPEED_AND_MODE:
                                        break;
                                    case GwCommands.CMD_GETSET_SYSTEM_DATE:
                                        break;
                                    case GwCommands.CMD_GETSET_IP_ADDRESS:
                                        break;
                                    case GwCommands.CMD_GETSET_IP_MASK:
                                        break;
                                    case GwCommands.CMD_GETSET_GATEWAY:
                                        break;
                                    case GwCommands.CMD_GETSET_WIFI_ON_OFF:
                                        break;
                                    case GwCommands.CMD_REBOOT:
                                        break;
                                    case GwCommands.CMD_GETSET_WIFI_STATION_MODE:
                                        break;
                                    case GwCommands.CMD_GETSET_AP_SSID:
                                        break;
                                    case GwCommands.CMD_GETSET_WIFI_PASSWORD:
                                        break;
                                    case GwCommands.CMD_DEVICE_HEALTH_PERIODIC:
                                        HeartBeatReceived(frame);
                                        break;
                                    case GwCommands.CMD_UPDATE_MSG_INFO:
                                        break;
                                }

                            }
                            else
                            {
                                FrameReceivedNotification(frame);
                            }
                        }

                    }
                    catch (Exception para)
                    {
                        throw new ArgumentException("Frame Missmatch in DeserualizeFrameBuffer " + para.Message);
                    }

                }
                else
                {
                    throw new ArgumentException("Start Of Frame is not supported");
                }

            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception in CANGW DLL - UDP Dispatcher: " + e.Message);
                return;
            }

            //Debug.WriteLine($"Received message {receivedBytes.Length} size");
        }
        protected virtual void FrameReceivedNotification(CanFrame receivedFrame)
        {
            FrameReceived?.Invoke(receivedFrame);
        }

        #region CONTROL_FRAME_METHODS
        private void HeartBeatReceived(CanFrame receivedFrame)
        {
            watchdog.Reset();
            
            /* Save vairables */
            //TODO: dispatch values
            OnGatewayStatusUpdated?.Invoke(0, 0, 0, 0);
        }
        #endregion CONTROL_FRAME_METHODS

        #endregion PRIVATE_METHODS

        #region PUBLIC_METHODS

        public void Connect()
        {
            //client.Start();
            control.Start();
            watchdog.Start();
        }

        public void Disconnect()
        {
            //client.Stop();
            watchdog.Stop();
        }

        public void sendSingleCanFrame(CanFrame pOutMsg)
        {
            try
            {
                byte[] data = FrameDeserializer.SerializeWithSOF(pOutMsg);
                byte[] udpRaw = COBS.Encode(data);
                control.SendMessage(udpRaw);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception in CANGW DLL - Send single frame:" + e.Message);
            }
        }

        public void addToMultipleFrame(CanFrame outFrame)
        {
            frames.Enqueue(outFrame);
        }

        public void FlushMultipleFrames()
        {
            try
            {

                if (frames.Count > 0)
                {
                    byte[] outData = FrameDeserializer.SerializeMultipleWithSOF(frames.ToArray());
                    frames.Clear();
                    byte[] udpData = COBS.Encode(outData);

                    control.SendMessage(udpData);
                    
                }
                else
                {
                    ; /* Out FIFO empty! */
                }
            }catch(Exception e)
            {
                Debug.WriteLine("Exception in CANGW DLL - Flush Frames:" + e.Message);
            }
        }


        public void ConfigurePort(int port)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
        #endregion PUBLIC_METHODS


    }
}
