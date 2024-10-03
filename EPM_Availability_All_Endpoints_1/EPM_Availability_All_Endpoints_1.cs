namespace EPM_Availability_All_Endpoints_1
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Skyline.DataMiner.Analytics.GenericInterface;
    using Skyline.DataMiner.Automation;
    using Skyline.DataMiner.Net.Messages;

    /// <summary>
    /// Represents a DataMiner Automation script.
    /// </summary>
    [GQIMetaData(Name = "All Endpoint Data")]
    public class CmData : IGQIDataSource, IGQIInputArguments, IGQIOnInit
    {
        private readonly GQIStringArgument frontEndElementArg = new GQIStringArgument("FE Element")
        {
            IsRequired = true,
        };

        private readonly GQIStringArgument systemTypeArg = new GQIStringArgument("System Type")
        {
            IsRequired = false,
        };

        private readonly GQIStringArgument systemNameArg = new GQIStringArgument("System Name")
        {
            IsRequired = false,
        };

        private GQIDMS _dms;

        private string frontEndElement = String.Empty;

        private string systemType = String.Empty;

        private string systemName = String.Empty;

        private List<GQIRow> listGqiRows = new List<GQIRow> { };

        private string systemTypeFilter = String.Empty;
        private int iterator = 0;
        private List<string> allCollectors = new List<string> { };

        public OnInitOutputArgs OnInit(OnInitInputArgs args)
        {
            _dms = args.DMS;
            return new OnInitOutputArgs();
        }

        public GQIArgument[] GetInputArguments()
        {
            return new GQIArgument[]
            {
                frontEndElementArg,
                systemTypeArg,
                systemNameArg,
            };
        }

        public GQIColumn[] GetColumns()
        {
            return new GQIColumn[]
            {
                new GQIStringColumn("Endpoint"),
                new GQIStringColumn("IP"),
                new GQIStringColumn("Customer Name"),
                new GQIStringColumn("Vendor Name"),
                new GQIDoubleColumn("Packet Loss Rate"),
                new GQIDoubleColumn("Jitter"),
                new GQIDoubleColumn("Latency"),
                new GQIDoubleColumn("RTT"),
            };
        }

        public GQIPage GetNextPage(GetNextPageInputArgs args)
        {

            return new GQIPage(listGqiRows.ToArray())
            {
                HasNextPage = false,
            };
        }

        public OnArgumentsProcessedOutputArgs OnArgumentsProcessed(OnArgumentsProcessedInputArgs args)
        {
            listGqiRows.Clear();
            try
            {
                frontEndElement = args.GetArgumentValue(frontEndElementArg);
                systemType = args.GetArgumentValue(systemTypeArg);
                systemName = args.GetArgumentValue(systemNameArg);

                allCollectors = GetAllCollectors();

                systemTypeFilter = GetSystemTypeFilter();
                if (String.IsNullOrEmpty(systemTypeFilter))
                {
                    return new OnArgumentsProcessedOutputArgs();
                }

                foreach (var collector in allCollectors)
                {
                    var collectorRows = GetTable(collector, 2000, new List<string>
                    {
                        systemTypeFilter,
                    });

                    Dictionary<string, EndpointOverview> endpointRows = ExtractCollectorData(collectorRows);
                    AddAllCableModems(endpointRows);
                }
            }
            catch
            {
                listGqiRows = new List<GQIRow>();
            }

            return new OnArgumentsProcessedOutputArgs();
        }

        public string GetSystemTypeFilter()
        {
            switch (systemType)
            {
                case "Customer":
                    return String.Format("forceFullTable=true;fullFilter=(2010=={0})", systemName);
                case "Vendor":
                    return String.Format("forceFullTable=true;fullFilter=(2011=={0})", systemName);
                case "Network":
                    return String.Format("forceFullTable=true;fullFilter=(2016=={0})", systemName);
                case "Region":
                    return String.Format("forceFullTable=true;fullFilter=(2015=={0})", systemName);
                case "Sub-Region":
                    return String.Format("forceFullTable=true;fullFilter=(2014=={0})", systemName);
                case "Hub":
                    return String.Format("forceFullTable=true;fullFilter=(2013=={0})", systemName);
                case "Station":
                    return String.Format("forceFullTable=true;fullFilter=(2012=={0})", systemName);
                default:
                    return String.Empty;
            }
        }

        public List<HelperPartialSettings[]> GetTable(string element, int tableId, List<string> filter)
        {
            var columns = new List<HelperPartialSettings[]>();

            var elementIds = element.Split('/');
            if (elementIds.Length > 1 && Int32.TryParse(elementIds[0], out int dmaId) && Int32.TryParse(elementIds[1], out int elemId))
            {
                // Retrieve client connections from the DMS using a GetInfoMessage request
                var getPartialTableMessage = new GetPartialTableMessage(dmaId, elemId, tableId, filter.ToArray());
                var paramChange = (ParameterChangeEventMessage)_dms.SendMessage(getPartialTableMessage);

                if (paramChange != null && paramChange.NewValue != null && paramChange.NewValue.ArrayValue != null)
                {
                    columns = paramChange.NewValue.ArrayValue
                        .Where(av => av != null && av.ArrayValue != null)
                        .Select(p => p.ArrayValue.Where(v => v != null)
                        .Select(c => new HelperPartialSettings
                        {
                            CellValue = c.CellValue.InteropValue,
                            DisplayValue = c.CellValue.CellDisplayValue,
                            DisplayType = c.CellDisplayState,
                        }).ToArray()).ToList();
                }
            }

            return columns;
        }

        public List<string> GetAllCollectors()
        {
            var collectorsTable = GetTable(frontEndElement, 700, new List<string>
            {
                "forceFullTable=true",
            });

            if (collectorsTable != null && collectorsTable.Any())
            {
                return collectorsTable[0].Select(x => Convert.ToString(x.CellValue)).ToList();
            }

            return new List<string>();
        }

        public static string ParseDoubleValue(double doubleValue, string unit)
        {
            if (doubleValue.Equals(-1))
            {
                return "N/A";
            }

            return Math.Round(doubleValue, 2) + " " + unit;
        }

        public static string ParseStringValue(string stringValue)
        {
            if (String.IsNullOrEmpty(stringValue) || stringValue == "-1")
            {
                return "N/A";
            }

            return stringValue;
        }

        private static Dictionary<string, EndpointOverview> ExtractCollectorData(List<HelperPartialSettings[]> collectorTable)
        {
            Dictionary<string, EndpointOverview> endpointRows = new Dictionary<string, EndpointOverview>();
            if (collectorTable != null && collectorTable.Any())
            {
                for (int i = 0; i < collectorTable[0].Count(); i++)
                {
                    var key = Convert.ToString(collectorTable[0][i].CellValue);
                    var oltRow = new EndpointOverview
                    {
                        Name = key,
                        Ip = Convert.ToString(collectorTable[1][i].CellValue),
                        CustomerName = Convert.ToString(collectorTable[9][i].CellValue),
                        VendorName = Convert.ToString(collectorTable[10][i].CellValue),
                        PacketLossRate = Convert.ToDouble(collectorTable[18][i].CellValue),
                        Jitter = Convert.ToDouble(collectorTable[16][i].CellValue),
                        Latency = Convert.ToDouble(collectorTable[17][i].CellValue),
                        Rtt = Convert.ToDouble(collectorTable[19][i].CellValue),
                    };

                    endpointRows[key] = oltRow;
                }
            }

            return endpointRows;
        }

        private void AddAllCableModems(Dictionary<string, EndpointOverview> oltRows)
        {
            foreach (var oltRow in oltRows.Values)
            {
                List<GQICell> listGqiCells = new List<GQICell>
                {
                    new GQICell
                    {
                        Value = oltRow.Name,
                    },
                    new GQICell
                    {
                        Value = oltRow.Ip,
                    },
                    new GQICell
                    {
                        Value = ParseStringValue(oltRow.CustomerName),
                    },
                    new GQICell
                    {
                        Value = ParseStringValue(oltRow.VendorName),
                    },
                    new GQICell
                    {
                        Value = oltRow.PacketLossRate,
                        DisplayValue = ParseDoubleValue(oltRow.PacketLossRate, "%"),
                    },
                    new GQICell
                    {
                        Value = oltRow.Jitter,
                        DisplayValue = ParseDoubleValue(oltRow.Jitter, "ms"),
                    },
                    new GQICell
                    {
                        Value = oltRow.Latency,
                        DisplayValue = ParseDoubleValue(oltRow.Latency, "ms"),
                    },
                    new GQICell
                    {
                        Value = oltRow.Rtt,
                        DisplayValue = ParseDoubleValue(oltRow.Rtt, "ms"),
                    },
                };

                var gqiRow = new GQIRow(listGqiCells.ToArray());

                listGqiRows.Add(gqiRow);
            }
        }
    }

    public class BackEndHelper
    {
        public string ElementId { get; set; }

        public string OLtId { get; set; }

        public string EntityId { get; set; }
    }

    public class HelperPartialSettings
    {
        public object CellValue { get; set; }

        public object DisplayValue { get; set; }

        public ParameterDisplayType DisplayType { get; set; }
    }

    public class EndpointOverview
    {
        public string Name { get; set; }

        public string Ip { get; set; }

        public string CustomerName { get; set; }

        public string VendorName { get; set; }

        public double PacketLossRate { get; set; }

        public double Jitter { get; set; }

        public double Latency { get; set; }

        public double Rtt { get; set; }
    }
}