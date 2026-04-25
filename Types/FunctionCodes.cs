using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace industREAL.CAN.CANGW.Types
{
    [Flags]
    public enum GwCommands : byte
    {
        CMD_GET_SERIAL_NUMBER        = (byte)'s', // 0x73
        CMD_GET_HARDWARE_VERSION     = (byte)'h',
        CMD_GET_FIRMWARE_VERSION     = (byte)'f',
        CMD_GETSET_TERMINATION       = (byte)'t',
        CMD_SET_TO_BOOTSTRAP         = (byte)'b',
        CMD_GETSET_IF_SPEED_AND_MODE = (byte)'l',
        CMD_GETSET_SYSTEM_DATE       = (byte)'d',
        CMD_GETSET_IP_ADDRESS        = (byte)'i',
        CMD_GETSET_IP_MASK           = (byte)'m',
        CMD_GETSET_GATEWAY           = (byte)'g',
        CMD_GETSET_WIFI_ON_OFF       = (byte)'w',
        CMD_REBOOT                   = (byte)'r',
        CMD_GETSET_WIFI_STATION_MODE = (byte)'c',
        CMD_GETSET_AP_SSID           = (byte)'n',
        CMD_GETSET_WIFI_PASSWORD     = (byte)'p',
        CMD_DEVICE_HEALTH_PERIODIC   = (byte)'1', // P:24
        CMD_UPDATE_MSG_INFO          = (byte)'u',
    }

}
