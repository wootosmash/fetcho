using Fetcho.Common;
using Fetcho.Common.Entities;
using Fetcho.Common.Net;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Fetcho
{
    class Program
    {
        /// <summary>
        /// Reads packets and runs custom classes on them
        /// </summary>
        /// <remarks>
        /// 
        /// USAGE:
        ///   reado [processor] [path] [args...]
        ///   reado --help
        ///   
        /// EXAMPLE:
        ///   reado ExtractLinksConsumer packet-53.xml
        ///   reado ExtractLinksThatMatchConsumer packet-2.xml australia news
        ///   reado QueryConsumer packet*.xml
        ///
        /// </remarks>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            ThrowIfAppSettingIsNotSet("FetchoWorkspaceServerBaseUri");
            ThrowIfAppSettingIsNotSet("DataSourcePaths");
            ThrowIfAppSettingIsNotSet("MLModelPath");
            SetupConfiguration().GetAwaiter().GetResult();

            if (args.Length < 2 || args[0] == "--help")
                OutputHelp();
            else
            {
                string packetProcessorString = args[0];
                string packetPath = args[1];

                var reado = CreateReadoProcessor(packetProcessorString, args);

                if (!packetPath.Contains("*"))
                {
                    reado.Process(packetPath).GetAwaiter().GetResult();
                }
                else
                {
                    string path = Path.GetDirectoryName(packetPath);
                    if (string.IsNullOrWhiteSpace(path))
                        path = Environment.CurrentDirectory;

                    string searchPattern = "*";
                    if (path.Length < packetPath.Length) searchPattern = packetPath.Substring(path.Length + 1);

                    foreach (var file in Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories).Randomise())
                    {
                        try
                        {
                            reado.Process(file).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            Utility.LogException(ex);
                        }
                    }
                }
            }
        }

        static ReadoProcessor CreateReadoProcessor(string processorType, string[] args)
        {
            var reado = new ReadoProcessor();
            reado.Processor.Consumer = GetWebDataPacketProcessor(processorType, GetConsumerArgs(args));
            return reado;
        }

        static IEnumerable<string> GetConsumerArgs(string[] args) => new ArraySegment<string>(args, 2, args.Length - 2);

        static WebDataPacketConsumer GetWebDataPacketProcessor(string typeName, IEnumerable<string> args)
        {
            var type = GetWebDataPacketConsumerTypes().FirstOrDefault(x => x.Name == typeName);

            if (type == null)
                throw new FetchoException("Can't find type with name " + typeName);

            return Activator.CreateInstance(type, args.ToArray()) as WebDataPacketConsumer;
        }

        static IEnumerable<Type> GetWebDataPacketConsumerTypes()
        {
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(assembly => assembly.GetExportedTypes())
                .Where(type => type.IsSubclassOf(typeof(WebDataPacketConsumer)));
            return types;
        }

        static void OutputHelp()
        {
            Console.WriteLine("reado [processor] [packet path] [args...]");
            Console.WriteLine("reado --help");
            Console.WriteLine();
            Console.WriteLine("Processors available:");
            OutputProcessors();
        }

        static void ThrowIfAppSettingIsNotSet(string key)
        {
            if (ConfigurationManager.AppSettings[key] == null)
                throw new FetchoException("{0} not set in the AppSettings section", key);
        }

        static void OutputProcessors()
        {
            var types = GetWebDataPacketConsumerTypes();

            foreach (var type in types)
                Console.WriteLine("{0}", type.Name);
        }

        static async Task<FetchoConfiguration> SetupConfiguration()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            log4net.Config.XmlConfigurator.Configure();
            var cfg = new FetchoConfiguration();
            FetchoConfiguration.Current = cfg;
            cfg.SetConfigurationSetting(
                () => cfg.FetchoWorkspaceServerBaseUri,
                ConfigurationManager.AppSettings["FetchoWorkspaceServerBaseUri"]
                );

            cfg.SetConfigurationSetting<IEnumerable<string>>(
                () => cfg.DataSourcePaths,
                ConfigurationManager.AppSettings["DataSourcePaths"].Split(',')
                );

            cfg.SetConfigurationSetting(
                () => cfg.MLModelPath,
                ConfigurationManager.AppSettings["MLModelPath"]
                );

            await GetOrCreateServerNode(cfg);

            return cfg;
        }

        static async Task GetOrCreateServerNode(FetchoConfiguration cfg)
        {
            if (cfg.FetchoWorkspaceServerBaseUri.Length == 0) return;

            var fetchoClient = new FetchoAPIV1Client(new Uri(cfg.FetchoWorkspaceServerBaseUri));

            cfg.CurrentServerNode = await fetchoClient.GetServerNodeAsync(Environment.MachineName);
            if (cfg.CurrentServerNode == null)
                cfg.CurrentServerNode = await fetchoClient.CreateServerNodeAsync(new ServerNode());
        }

    }
}
