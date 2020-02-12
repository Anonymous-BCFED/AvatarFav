using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRCModLoader;

namespace AvatarFav.Model
{
    public class SimpleAvatarList
    {
        public const int CURRENT_VERSION = 1;

        public int version;
        public string[] avatarIDs;

        public static SimpleAvatarList ParseJSON(string json)
        {
            SimpleAvatarList o = null;
            VRCModLogger.Log("[AvatarFavLocal] Received " + json);
            var root = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (root.ContainsKey("list"))
            {
                VRCModLogger.Log("[AvatarFavLocal] Detected version 0 schema, upgrading to 1");
                // Version 0
                var avs = new List<string>();
                var avlist = JsonConvert.DeserializeObject<SerializableApiAvatarList>(json);
                o = new SimpleAvatarList();
                o.version = 1;
                o.avatarIDs = (from av in avlist.list select av.id).ToArray();
            }

            if (root.ContainsKey("version"))
            {
                //System.Diagnostics.Debug.WriteLine(root["version"].GetType().FullName);
                var version = (int)(Int64)root["version"];
                if (version == CURRENT_VERSION)
                {
                    VRCModLogger.Log($"[AvatarFavLocal] Detected version {CURRENT_VERSION} schema. Yay!");
                    o = JsonConvert.DeserializeObject<SimpleAvatarList>(json);
                }
            }

            return o;
        }
    }
}
