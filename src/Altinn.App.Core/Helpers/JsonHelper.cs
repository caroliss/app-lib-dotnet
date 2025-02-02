#nullable enable

using Newtonsoft.Json.Linq;

namespace Altinn.App.Core.Helpers
{
    /// <summary>
    /// Helper class for processing JSON objects
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>
        /// Find changed fields between old and new json objects
        /// </summary>
        /// <param name="oldJson">The old JSON object</param>
        /// <param name="currentJson">The new JSON object</param>
        /// <returns>Key-value pairs of the changed fields</returns>
        public static Dictionary<string, object?> FindChangedFields(string oldJson, string currentJson)
        {
            JToken old = JToken.Parse(oldJson);
            JToken current = JToken.Parse(currentJson);
            Dictionary<string, object?> dict = new Dictionary<string, object?>();
            FindDiff(dict, old, current, string.Empty);
            return dict;
        }

        private static void FindDiff(Dictionary<string, object?> dict, JToken? old, JToken? current, string prefix)
        {
            if (JToken.DeepEquals(old, current))
            {
                return;
            }

            int index = 0;
            JArray? oldArray = old as JArray;
            JObject? currentObj = current as JObject;
            JObject? oldObj = old as JObject;

            switch (current?.Type)
            {
                case JTokenType.Object:

                    if (oldArray != null)
                    {
                        for (index = 0; index < oldArray.Count; index++)
                        {
                            dict.Add($"{prefix}[{index}]", null);
                        }
                    }
                    else if (old?.Type != JTokenType.Object)
                    {
                        // Scalar values would use the plain prefix, but object create deeper prefixes. If a scalar
                        // value is replaced by an object, we need to unset the scalar value as well.
                        dict.Add(prefix, null);
                    }

                    if (oldObj == null && currentObj != null)
                    {
                        foreach (string key in currentObj.Properties().Select(c => c.Name))
                        {
                            FindDiff(dict, JValue.CreateNull(), currentObj[key], Join(prefix, key));
                        }

                        break;
                    }

                    if (oldObj != null && currentObj != null)
                    {
                        IEnumerable<string> addedKeys = currentObj.Properties().Select(c => c.Name).Except(oldObj.Properties().Select(c => c.Name));
                        IEnumerable<string> removedKeys = oldObj.Properties().Select(c => c.Name).Except(currentObj.Properties().Select(c => c.Name));
                        IEnumerable<string> unchangedKeys = currentObj.Properties().Where(c => JToken.DeepEquals(c.Value, oldObj[c.Name])).Select(c => c.Name);
                        foreach (string key in addedKeys)
                        {
                            FindDiff(dict, JValue.CreateNull(), currentObj[key], Join(prefix, key));
                        }

                        foreach (string key in removedKeys)
                        {
                            FindDiff(dict, oldObj[key], JValue.CreateNull(), Join(prefix, key));
                        }

                        var potentiallyModifiedKeys = currentObj.Properties().Select(c => c.Name).Except(addedKeys).Except(unchangedKeys);
                        foreach (var key in potentiallyModifiedKeys)
                        {
                            FindDiff(dict, oldObj[key], currentObj[key], Join(prefix, key));
                        }
                    }

                    break;

                case JTokenType.Array:
                    if (oldArray != null)
                    {
                        foreach (var value in current.Children())
                        {
                            FindDiff(
                                dict,
                                oldArray?.Count - 1 >= index ? oldArray?[index] : new JObject(),
                                value,
                                $"{prefix}[{index}]");

                            index++;
                        }

                        while (index < oldArray?.Count)
                        {
                            FindDiff(dict, oldArray[index], JValue.CreateNull(), $"{prefix}[{index}]");
                            index++;
                        }
                    }
                    else
                    {
                        if (old?.Type == JTokenType.Object)
                        {
                            FindDiff(dict, old, JValue.CreateNull(), prefix);
                        }

                        foreach (JToken value in current.Children())
                        {
                            FindDiff(dict, JValue.CreateNull(), value, $"{prefix}[{index}]");
                            index++;
                        }
                    }

                    break;

                case JTokenType.Null:
                    if (oldObj != null)
                    {
                        foreach (string key in oldObj.Properties().Select(c => c.Name))
                        {
                            FindDiff(dict, oldObj[key], JValue.CreateNull(), Join(prefix, key));
                        }
                    }
                    else if (old?.Type == JTokenType.Array)
                    {
                        for (index = 0; index < oldArray?.Count; index++)
                        {
                            dict.Add($"{prefix}[{index}]", null);
                        }
                    }
                    else
                    {
                        dict.Add(prefix, ((JValue)current).Value);
                    }

                    break;

                default:
                    dict.Add(prefix, current == null ? null : ((JValue)current).Value);
                    break;
            }
        }

        private static string Join(string prefix, string name)
        {
            return string.IsNullOrEmpty(prefix) ? name : prefix + "." + name;
        }
    }
}
