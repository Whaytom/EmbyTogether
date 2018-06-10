using System;
using MediaBrowser.Model.Plugins;

namespace embytogether
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public String[] SelectedGUIDs { get; set; }
        public int Timeoffset_seek { get; set; }
        public double Timeoffset_cmd_same_user { get; set; }
        public double Timeoffset_cmd_diff_user { get; set; }


        public PluginConfiguration()
        {
            Timeoffset_seek = 10;
            Timeoffset_cmd_same_user = 1;
            Timeoffset_cmd_diff_user = 3;
        }
    }
}
