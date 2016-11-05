﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using EDEngineer.Models;
using EDEngineer.Models.Operations;
using EDEngineer.Utils.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodaTime.Text;

namespace EDEngineer.Utils
{
    public class JournalEntryConverter : JsonConverter
    {
        private readonly ItemNameConverter converter;
        private readonly ISimpleDictionary<string, Entry> entries;

        public JournalEntryConverter(ItemNameConverter converter, ISimpleDictionary<string, Entry> entries)
        {
            this.converter = converter;
            this.entries = entries;
        }

        public override bool CanWrite => true;

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof (JournalEntry);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            reader.DateParseHandling = DateParseHandling.None;
            var data = JObject.Load(reader);

            var entry = new JournalEntry
            {
                TimeStamp = InstantPattern.GeneralPattern.Parse((string)data["timestamp"]).Value,
                OriginalJson = data.ToString()
            };

            JournalEvent journalEvent;

            try
            {
                journalEvent = data["event"].ToObject<JournalEvent>(serializer);
            }
            catch (JsonSerializationException)
            {
                return entry;
            }

            try
            {
                entry.JournalOperation = ExtractOperation(data, journalEvent);
            }
            catch (Exception e)
            {
                MessageBox.Show(
                    $"Something went wrong in parsing your logs, open an issue on GitHub with this information : {Environment.NewLine}" +
                    $"LogEntry = {data}{Environment.NewLine}" +
                    $"Error:{e}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (entry.JournalOperation != null)
            {
                entry.JournalOperation.JournalEvent = journalEvent;
            }

            return entry;
        }

        private JournalOperation ExtractOperation(JObject data, JournalEvent journalEvent)
        {
            switch (journalEvent)
            {
                case JournalEvent.ManualUserChange:
                    return ExctractManualOperation(data);
                case JournalEvent.MiningRefined:
                    return ExtractMiningRefined(data);
                case JournalEvent.EngineerCraft:
                    return ExtractEngineerOperation(data);
                case JournalEvent.MarketSell:
                    return ExtractMarketSell(data);
                case JournalEvent.MarketBuy:
                    return ExtractMarketBuy(data);
                case JournalEvent.MaterialDiscarded:
                    return ExtractMaterialDiscarded(data);
                case JournalEvent.MaterialCollected:
                    return ExtractMaterialCollected(data);
                case JournalEvent.MissionCompleted:
                    return ExtractMissionCompleted(data);
                case JournalEvent.CollectCargo:
                    return ExtractCollectCargo(data);
                case JournalEvent.EjectCargo:
                    return ExtractEjectCargo(data);
                case JournalEvent.Synthesis:
                    return ExtractSynthesis(data);
                default:
                    return null;
            }
        }
        private JournalOperation ExtractMarketSell(JObject data)
        {
            string marketSellName;
            if (!converter.TryGet((string) data["Type"], out marketSellName))
            {
                return null;
            }

            return new CargoOperation
            {
                CommodityName = marketSellName,
                Size = -1*data["Count"]?.ToObject<int>() ?? -1
            };
        }

        private JournalOperation ExtractMiningRefined(JObject data)
        {
            string miningRefinedName;
            if (!converter.TryGet((string)data["Type"], out miningRefinedName))
            {
                return null;
            }

            return new CargoOperation
            {
                CommodityName = miningRefinedName,
                Size = 1
            };
        }

        private JournalOperation ExtractMarketBuy(JObject data)
        {
            string marketBuyName;
            if (!converter.TryGet((string)data["Type"], out marketBuyName))
            {
                return null;
            }

            return new CargoOperation
            {
                CommodityName = marketBuyName,
                Size = data["Count"]?.ToObject<int>() ?? 1
            };
        }

        private JournalOperation ExtractEjectCargo(JObject data)
        {
            string ejectCargoName;
            if (!converter.TryGet((string)data["Type"], out ejectCargoName))
            {
                return null;
            }

            return new CargoOperation
            {
                CommodityName = ejectCargoName,
                Size = -1
            };
        }

        private JournalOperation ExtractCollectCargo(JObject data)
        {
            string collectCargoName;
            if (!converter.TryGet((string) data["Type"], out collectCargoName))
            {
                return null;
            }

            return new CargoOperation
            {
                CommodityName = collectCargoName,
                Size = 1
            };
        }

        private MissionCompletedOperation ExtractMissionCompleted(JObject data)
        {
            JToken rewardData;

            if (!data.TryGetValue("CommodityReward", out rewardData))
            {
                return null;
            }

            var missionCompleted = new MissionCompletedOperation
            {
                CommodityRewards = rewardData
                    .Select(c =>
                    {
                        string rewardName;
                        return Tuple.Create(c,
                            converter.TryGet((string) c["Name"], out rewardName),
                            rewardName);
                    })
                    .Where(c => c.Item2)
                    .Select(c =>
                    {
                        var r = new CargoOperation
                        {
                            CommodityName = c.Item3,
                            Size = c.Item1["Count"]?.ToObject<int>() ?? 1,
                            JournalEvent = JournalEvent.MissionCompleted
                        };
                        return r;
                    }).ToList()
            };

            return missionCompleted.CommodityRewards.Any() ? missionCompleted : null;
        }

        private JournalOperation ExtractMaterialDiscarded(JObject data)
        {
            string materialDiscardedName;
            if (!converter.TryGet((string) data["Name"], out materialDiscardedName))
            {
                MessageBox.Show($"Unknown material, please contact the author ! {(string) data["Name"]}");
                return null;
            }

            if (((string) data["Category"]).ToLowerInvariant() == "encoded")
            {
                return new DataOperation
                {
                    DataName = materialDiscardedName,
                    Size = -1*data["Count"]?.ToObject<int>() ?? -1
                };
            }
            else // Manufactured & Raw
            {
                return new MaterialOperation
                {
                    MaterialName = materialDiscardedName,
                    Size = -1*data["Count"]?.ToObject<int>() ?? -1
                };
            }
        }

        private JournalOperation ExtractSynthesis(JObject data)
        {
            foreach (var jToken in data["Materials"])
            {
                var material = (JProperty) jToken;
                string synthesisIngredientName;
                if (!converter.TryGet(material.Name, out synthesisIngredientName))
                {
                    MessageBox.Show($"Unknown material, please contact the author ! {material.Name}");
                    return null;
                }

                var entry = converter[synthesisIngredientName];

                switch (entry.Kind)
                {
                    case Kind.Material:
                        return new MaterialOperation()
                        {
                            MaterialName = synthesisIngredientName,
                            Size = material.Value != null ? (int) material.Value : 1
                        };
                    case Kind.Data:
                        return new DataOperation()
                        {
                            DataName = synthesisIngredientName,
                            Size = material.Value != null ? (int)material.Value : 1
                        };
                    case Kind.Commodity:
                        return new CargoOperation()
                        {
                            CommodityName = synthesisIngredientName,
                            Size = material.Value != null ? (int)material.Value : 1
                        };
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return null;
        }

        private JournalOperation ExtractMaterialCollected(JObject data)
        {
            string materialCollectedName;
            if (!converter.TryGet((string) data["Name"], out materialCollectedName))
            {
                MessageBox.Show($"Unknown material, please contact the author ! {(string) data["Name"]}");
                return null;
            }

            if (((string) data["Category"]).ToLowerInvariant() == "encoded")
            {
                return new DataOperation
                {
                    DataName = materialCollectedName, Size = data["Count"]?.ToObject<int>() ?? 1
                };
            }
            else // Manufactured & Raw
            {
                return new MaterialOperation
                {
                    MaterialName = materialCollectedName, Size = data["Count"]?.ToObject<int>() ?? 1
                };
            }
        }

        private EngineerOperation ExtractEngineerOperation(JObject data)
        {
            var operation = new EngineerOperation
            {
                IngredientsConsumed = data["Ingredients"].Select(c =>
                {
                    dynamic cc = c;
                    string rewardName;
                    return Tuple.Create(converter.TryGet((string) cc.Name, out rewardName), rewardName, (int) cc.Value);
                }).Where(c => c.Item1).Select(c => new BlueprintIngredient(entries[c.Item2], c.Item3)).ToList()
            };

            return operation.IngredientsConsumed.Any() ? operation : null;
        }

        private static ManualChangeOperation ExctractManualOperation(JObject data)
        {
            return new ManualChangeOperation
            {
                Name = (string) data["Name"], Count = (int) data["Count"]
            };
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var entry = (JournalEntry) value;
            var operation = (ManualChangeOperation) entry.JournalOperation;
            writer.WriteStartObject();
            writer.WritePropertyName("timestamp");
            writer.WriteValue(entry.TimeStamp.ToString(InstantPattern.GeneralPattern.PatternText, CultureInfo.InvariantCulture));
            writer.WritePropertyName("event");
            writer.WriteValue(operation.JournalEvent.ToString());
            writer.WritePropertyName("Name");
            writer.WriteValue(operation.Name);
            writer.WritePropertyName("Count");
            writer.WriteValue(operation.Count);
            writer.WriteEndObject();
        }
    }
}