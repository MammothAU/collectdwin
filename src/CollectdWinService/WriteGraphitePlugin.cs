using System;
using NLog;
using System.Configuration;
using System.Net;
using System.Collections.Generic;
using System.Net.Sockets;
using BloombergFLP.CollectdWin;

namespace BloombergFLP.CollectdWin
{
	internal class WriteGraphitePlugin : IMetricsWritePlugin
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private string _host;
        private int _port;
        private string _prefix;

		private const char EscapeChar = '_';
		private const string GraphiteForbidden = " \t\"\\:!/()\n\r.";
		private static object _lock = new object();
		private TcpClient _client;
		private NetworkStream _clientStream;
        public void Configure()
        {
			var config = ConfigurationManager.GetSection("WriteGraphite") as WriteGraphitePluginConfig;
            if (config == null)
            {
				throw new Exception("Cannot get configuration section : WriteGraphite");
            }

            _host = config.Host;
            _prefix = config.Prefix;
            _port = config.Port;

			Logger.Info("WriteGraphite Posting to: {0}:{1}", _host, _port);
            Logger.Info("with prefix: {0}", _prefix);
	   
        }

        public void Start()
        {
	        EnsureConnection();
            Logger.Info("WriteCollectdPlugin started");
        }

        public void Stop()
        {
	        lock (_lock)
	        {
		        if (_clientStream != null)
		        {
			        _clientStream.Close();
		        }
		        if (_client != null)
		        {
			        _client.Close();
		        }
	        }
	        Logger.Info("WriteCollectdPlugin stopped");
        }

		private bool EnsureConnectionNoLock()
		{
			if (_client == null || !_client.Connected)
			{
				if (_client != null)
				{
					if (_clientStream != null)
					{
						_clientStream.Close();
						_clientStream = null;
					}
					_client.Close();
					_client = null;
				}

				try
				{
					_client = new TcpClient();
					_client.SendTimeout = 1000;
					//todo add connection retry
					_client.Connect(_host, _port);
					_clientStream = _client.GetStream();
				}
				catch (Exception e)
				{
					Logger.ErrorException(String.Format("Error connecting to host {0}:{1}", _host, _port), e);
					if (_client != null)
					{
						_client.Close();
						_client = null;
					}
					if (_clientStream != null)
					{
						_clientStream.Close();
						_clientStream = null;
					}
					return false;
				}
			}
			return true;
		}

		private bool EnsureConnection()
		{
			lock (_lock)
			{
				return EnsureConnectionNoLock();
			}
		}


		private string EscapeGraphiteSegment(string segment)
		{
			string escaped = segment;
			foreach (char forbidden in GraphiteForbidden)
			{
				escaped = escaped.Replace(forbidden,EscapeChar);
			}
			return escaped;
		}
		


		private string FormatName(MetricValue metric, string dsName)
		{
			var name = _prefix;
			name = name + "." + EscapeGraphiteSegment(metric.HostName);

			name = name + "." + EscapeGraphiteSegment(metric.PluginName);
			if (!String.IsNullOrEmpty(metric.PluginInstanceName))
			{
				name = name + "-" + EscapeGraphiteSegment(metric.PluginInstanceName);
			}
			
			name = name + "." + EscapeGraphiteSegment(metric.TypeName);
			if (!String.IsNullOrEmpty(metric.TypeInstanceName))
			{
				name = name + "-" + EscapeGraphiteSegment(metric.TypeInstanceName);
			}

			if (!String.IsNullOrEmpty(dsName))
			{
				name = name + "." + EscapeGraphiteSegment(dsName);
			}

			return name;
		}

		

		public void Write(MetricValue metric)
        {
			var dsList = DataSetCollection.Instance.GetDataSource(metric.TypeName);
			try
			{
				for (var i = 0; i < dsList.Count; i++)
				{
					var dsName = dsList[i].Name;
					var name = FormatName(metric, dsList.Count > 1 ? dsName : "");
					var value = metric.Values[i].ToString("G");
					var timestamp = Convert.ToInt64(metric.Epoch).ToString("d");

					var message = String.Format("{0} {1} {2}\n", name, value, timestamp);
					byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
					
					
					lock (_lock)
					{
						if (EnsureConnectionNoLock())
						{
							_clientStream.Write(data, 0, data.Length);
						}
					}
				}

			}
			catch (SocketException e)
			{
				Logger.ErrorException("Error writing to host", e);
			}
			catch (Exception e)
			{
				Logger.ErrorException("Error writing to host",e);
			}
        }

        
    }
}

// ----------------------------------------------------------------------------
// Copyright (C) 2015 Mammoth Pty Ltd.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// ----------------------------- END-OF-FILE ----------------------------------