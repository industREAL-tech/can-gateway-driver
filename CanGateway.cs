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

// Both industREAL.CAN.CANGW.Utils and industREAL.CAN.CORE declare a `Log`, so an
// unqualified use here is CS0104. Alias to the CORE facade, which is the single
// sink the whole stack now writes to (see Utils/iLogger.cs).
using Log = industREAL.CAN.CORE.Log;

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
        #region CONSTANTS
        /// <summary>Serialised size of a frame record's fixed part: canId, flags,
        /// msgUid, dataLength, canChannel. Payload follows.</summary>
        private const int FrameHeaderLength = 14;

        /// <summary>The batch count is a single byte on the wire.</summary>
        private const int MaxFramesPerDatagram = 255;

        /// <summary>One Ethernet MTU of UDP payload, pre-COBS. The gateway reads
        /// only the first pbuf of a datagram, so a fragmented one is truncated.</summary>
        private const int MaxDatagramPayload = 1400;
        #endregion CONSTANTS

        #region LOCALS
        readonly AsyncUDPClient              control;
        readonly ConcurrentQueue <CanFrame>  frames;
        readonly Watchdog                    watchdog;
        bool                                 disposed;
        public bool                 isConnected { get; private set; }
        #endregion LOCALS

        #region EVENTS

        public delegate void CanFrameReceived(CanFrame receivedFrame);
        public delegate void GatewayStatusUpdate(byte TEC_A, byte REC_A, byte TEC_B, byte REC_B);
        public delegate void ConnectionChanged(bool isConnected);

        public event CanFrameReceived?    FrameReceived;
        public event GatewayStatusUpdate? OnGatewayStatusUpdated;
        public event ConnectionChanged?   OnConnectionChanged;
        #endregion EVENTS

        #region CONSTUCTOR
        public CanGateway(string serverIp, int serverPort)
        {
            isConnected = false;
            frames      = new ConcurrentQueue<CanFrame>();

            // serverPort is the gateway's BROADCAST_PORT (12144 on dev_UNICAST);
            // its receive socket is bound to BROADCAST_PORT + 1. The gateway
            // replies to our source address and port, so this one socket carries
            // both directions - which is why AsyncUDPBroadcastClient, which bound
            // a fixed port to catch the old subnet broadcast, is no longer used.
            control     = new AsyncUDPClient(serverIp, serverPort+1);
            watchdog    = new(TimeSpan.FromMilliseconds(800),true);

            control.NotifyListeners += Client_NotifyListeners;
            control.OnReceiveError  += ex => Log.Error(ex, "CANGW: UDP receive failed");
            watchdog.TimedOut       += Watchdog_TimedOut;
        }

        public CanGateway(string pPort, uint pBaudrate)
        {
            throw new NotImplementedException();
        }
        #endregion CONSTUCTOR

        #region PRIVATE_METHODS
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
        /// <summary>Transmit/receive error counters from the last device-health
        /// frame, and whether either channel has reached error-passive.</summary>
        public byte TEC_A { get; private set; }
        public byte REC_A { get; private set; }
        public byte TEC_B { get; private set; }
        public byte REC_B { get; private set; }
        public bool ErrorPassiveA { get; private set; }
        public bool ErrorPassiveB { get; private set; }

        /// <summary>
        /// Bus supervision counters, present on firmware that reports a 12-byte
        /// device-health frame. Zero on older firmware.
        /// </summary>
        /// <remarks>
        /// BusOffRecoveries counts how many times the gateway had to clear a
        /// bus-off condition on that channel and rejoin - without the firmware's
        /// recovery the channel would have stayed dead until power cycle.
        /// TxAborts counts transmissions cancelled because they were pending long
        /// enough to prove nothing would ever acknowledge them; each one is a
        /// frame that did NOT reach the bus, and a non-zero value means that
        /// channel has no working partner or a badly degraded bus.
        /// </remarks>
        public byte BusOffRecoveriesA { get; private set; }
        public byte BusOffRecoveriesB { get; private set; }
        public byte TxAbortsA { get; private set; }
        public byte TxAbortsB { get; private set; }

        /// <summary>Whether the channel is in bus-off right now.</summary>
        public bool BusOffA { get; private set; }
        public bool BusOffB { get; private set; }

        private void HeartBeatReceived(CanFrame receivedFrame)
        {
            watchdog.Reset();

            // The firmware reports HAL_FDCAN_GetErrorCounters() values in this
            // layout. The payload used to be discarded entirely and the event
            // raised with (0,0,0,0), which made the bus-state features the
            // TO-DO list asks for unimplementable.
            var d = receivedFrame._data;
            if (d.Length >= 4)
            {
                TEC_A = d[0];
                REC_A = d[1];
                TEC_B = d[2];
                REC_B = d[3];
            }
            if (d.Length >= 6)
            {
                ErrorPassiveA = d[4] != 0;
                ErrorPassiveB = d[5] != 0;
            }

            // Bytes 6..10 were added with firmware bus supervision. Length-guarded
            // so an older gateway that still sends 8 bytes keeps working.
            if (d.Length >= 11)
            {
                BusOffRecoveriesA = d[6];
                BusOffRecoveriesB = d[7];
                TxAbortsA         = d[8];
                TxAbortsB         = d[9];
                BusOffA           = (d[10] & 0x01) != 0;
                BusOffB           = (d[10] & 0x02) != 0;
            }

            OnGatewayStatusUpdated?.Invoke(TEC_A, REC_A, TEC_B, REC_B);
        }
        #endregion CONTROL_FRAME_METHODS

        #endregion PRIVATE_METHODS

        #region PUBLIC_METHODS

        public void Connect()
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            control.Start();
            watchdog.Start();
        }

        /// <summary>
        /// Stops the receiver and the watchdog. Connect() may be called again
        /// afterwards - previously this left the UDP receive loop running, and
        /// AsyncUDPClient could not be restarted once stopped, so a disconnect
        /// was permanent.
        /// </summary>
        public void Disconnect()
        {
            watchdog.Stop();
            control.Stop();

            if (isConnected)
            {
                isConnected = false;
                OnConnectionChanged?.Invoke(isConnected);
            }
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
                Log.Error(e, "CANGW: send single frame failed");
            }
        }

        public void addToMultipleFrame(CanFrame outFrame)
        {
            frames.Enqueue(outFrame);
        }

        /// <summary>
        /// Sends everything queued by addToMultipleFrame(), in batches the
        /// gateway can actually accept.
        /// </summary>
        public void FlushMultipleFrames()
        {
            try
            {
                // Drain the queue rather than ToArray() + Clear(): those are two
                // separate operations, so anything another thread enqueued
                // between them used to be discarded without ever being sent.
                var batch = new List<CanFrame>(MaxFramesPerDatagram);
                int batchBytes = 1 + 1;         // 'm' + count

                while (frames.TryDequeue(out var frame))
                {
                    int frameBytes = FrameHeaderLength + (byte)frame._dataLength;

                    // Two hard limits, both of which the old code ignored:
                    //  - the count field is a single byte, so 256 queued frames
                    //    wrapped to 0 and the gateway transmitted nothing while
                    //    the caller believed the batch had been flushed;
                    //  - the gateway decodes into a fixed buffer and only reads
                    //    the first pbuf of the datagram, so an over-MTU batch is
                    //    truncated or rejected.
                    if (batch.Count == MaxFramesPerDatagram ||
                        batchBytes + frameBytes > MaxDatagramPayload)
                    {
                        SendBatch(batch);
                        batch.Clear();
                        batchBytes = 1 + 1;
                    }

                    batch.Add(frame);
                    batchBytes += frameBytes;
                }

                if (batch.Count > 0)
                {
                    SendBatch(batch);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "CANGW: flush frames failed");
            }
        }

        private void SendBatch(List<CanFrame> batch)
        {
            byte[] outData = FrameDeserializer.SerializeMultipleWithSOF(batch.ToArray());
            control.SendMessage(COBS.Encode(outData));
        }

        public void ConfigurePort(int port)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            // Release-note <0.3.1> claims "Proper Disposing with IDisposable",
            // but this used to throw NotImplementedException, so any `using` or
            // container-managed lifetime crashed at scope exit.
            try { watchdog.Stop(); } catch (Exception e) { Log.Warn("CANGW: watchdog stop failed: " + e.Message); }

            watchdog.TimedOut -= Watchdog_TimedOut;
            control.NotifyListeners -= Client_NotifyListeners;

            watchdog.Dispose();
            control.Dispose();

            isConnected = false;
            GC.SuppressFinalize(this);
        }
        #endregion PUBLIC_METHODS


    }
}
