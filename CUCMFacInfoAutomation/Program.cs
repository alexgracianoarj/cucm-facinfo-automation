using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Configuration;
using WinSCP;
using log4net;
using System.Net;
using System.Xml.Linq;
using RestSharp;
using CsvHelper;
using System.Globalization;
using System.Collections.Specialized;
using System.Collections.Generic;

namespace CUCMFacInfoAutomation
{
    class Program
    {
        readonly ILog Log = LogManager.GetLogger("CUCMFacInfoAutomation");
        SessionOptions sessionOptions;
        XNamespace nsSoap;
        XNamespace nsCucm;
        NameValueCollection appSettings;

        static void Main(string[] args)
        {
            Program program = new Program();
            program.Start();
        }

        void Start()
        {
            log4net.Config.XmlConfigurator.Configure();
            appSettings = ConfigurationManager.AppSettings;

            sessionOptions = new SessionOptions
            {
                //Protocol = Protocol.Sftp,
                Protocol = Protocol.Ftp,
                Timeout = new TimeSpan(0, 2, 0),
                HostName = appSettings["ip_ftp"],
                UserName = appSettings["login_ftp"],
                Password = appSettings["senha_ftp"],
                PortNumber = int.Parse(appSettings["port_ftp"]),
                //SshHostKeyFingerprint = appSettings["SFTPhostKey"],
            };

            nsSoap = "http://schemas.xmlsoap.org/soap/envelope/";
            nsCucm = "http://www.cisco.com/AXL/API/" + appSettings["VERSAO"];

            while (true)
            {
                GetFiles();

                DirectoryInfo directoryInfo = new DirectoryInfo("temp");
                FileInfo[] files = directoryInfo.GetFiles("*.csv");

                if (files.Length > 0)
                {
                    foreach (FileInfo file in files)
                    {
                        var reader = new StreamReader(file.FullName);
                        var csvReader = new CsvReader(reader, CultureInfo.InvariantCulture);
                        csvReader.Configuration.HasHeaderRecord = false;
                        csvReader.Configuration.Delimiter = ",";
                        List<Rlt> rlts = csvReader.GetRecords<Rlt>().ToList();
                        reader.Close();
                        File.Delete(file.FullName);

                        foreach (Rlt rlt in rlts)
                        {
                            Fac fac = new Fac
                            {
                                Name = rlt.Matricula,
                                Code = rlt.Senha,
                                AuthorizationLevel = rlt.Categoria
                            };

                            if (rlt.Acao.Equals("1"))
                            {
                                AddFac(fac);
                                var writer = new StreamWriter($@"temp\{Path.GetFileNameWithoutExtension(file.Name)}_inseridos.csv", true);
                                var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
                                csvWriter.WriteRecord(rlt);
                                csvWriter.NextRecord();
                                writer.Close();
                            }
                            else if (rlt.Acao.Equals("2"))
                            {
                                RemoveFac(fac);
                                var writer = new StreamWriter($@"temp\{Path.GetFileNameWithoutExtension(file.Name)}_excluidos.csv", true);
                                var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
                                csvWriter.WriteRecord(rlt);
                                csvWriter.NextRecord();
                                writer.Close();
                            }
                            else
                            {
                                UpdateFac(fac);
                                var writer = new StreamWriter($@"temp\{Path.GetFileNameWithoutExtension(file.Name)}_alterados.csv", true);
                                var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
                                csvWriter.WriteRecord(rlt);
                                csvWriter.NextRecord();
                                writer.Close();
                            }
                        }
                    }

                    PutFiles();
                }

                Thread.Sleep(int.Parse(appSettings["sleep"]));
            }
        }

        void AddFac(Fac fac)
        {
            try
            {
                IRestResponse response = CiscoAXL($@"
<soapenv:Envelope xmlns:soapenv=""{nsSoap}"" xmlns:ns=""{nsCucm}"">
   <soapenv:Header/>
   <soapenv:Body>
      <ns:addFacInfo sequence=""?"">
         <facInfo>
            <name>{fac.Name}</name>
            <code>{fac.Code}</code>
            <authorizationLevel>{fac.AuthorizationLevel}</authorizationLevel>
         </facInfo>
      </ns:addFacInfo>
   </soapenv:Body>
</soapenv:Envelope>
                ");
                if (!response.StatusCode.ToString().Equals("OK"))
                {
                    var resXml = XDocument.Parse(response.Content);
                    var error = resXml.Descendants(nsSoap + "Body").First().Element(nsSoap + "Fault").Element("faultstring").Value;
                    Log.Error($"CUCM: Error on try add FacInfo {fac.Name}, message={error}");
                }
                else
                {
                    var resXml = XDocument.Parse(response.Content);
                    var @return = resXml.Descendants(nsSoap + "Body").First().Element(nsCucm + "addFacInfoResponse").Element("return").Value;
                    Log.Info($"CUCM: Success on add FacInfo {fac.Name}, return={@return}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CUCM: Error on try add FacInfo {fac.Name}, message={ex.Message}");
            }
        }

        void UpdateFac(Fac fac)
        {
            try
            {
                IRestResponse response = CiscoAXL($@"
<soapenv:Envelope xmlns:soapenv=""{nsSoap}"" xmlns:ns=""{nsCucm}"">
   <soapenv:Header/>
   <soapenv:Body>
      <ns:updateFacInfo sequence=""?"">
         <name>{fac.Name}</name>
         <code>{fac.Code}</code>
         <authorizationLevel>{fac.AuthorizationLevel}</authorizationLevel>
      </ns:updateFacInfo>
   </soapenv:Body>
</soapenv:Envelope>
                ");
                if (!response.StatusCode.ToString().Equals("OK"))
                {
                    var resXml = XDocument.Parse(response.Content);
                    var error = resXml.Descendants(nsSoap + "Body").First().Element(nsSoap + "Fault").Element("faultstring").Value;
                    Log.Error($"CUCM: Error on try update FacInfo {fac.Name}, message={error}");
                }
                else
                {
                    var resXml = XDocument.Parse(response.Content);
                    var @return = resXml.Descendants(nsSoap + "Body").First().Element(nsCucm + "updateFacInfoResponse").Element("return").Value;
                    Log.Info($"CUCM: Success on update FacInfo {fac.Name}, return={@return}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CUCM: Error on try update FacInfo {fac.Name}, message={ex.Message}");
            }
        }

        void RemoveFac(Fac fac)
        {
            try
            {
                IRestResponse response = CiscoAXL($@"
<soapenv:Envelope xmlns:soapenv=""{nsSoap}"" xmlns:ns=""{nsCucm}"">
   <soapenv:Header/>
   <soapenv:Body>
      <ns:removeFacInfo sequence=""?"">
         <name>{fac.Name}</name>
      </ns:removeFacInfo>
   </soapenv:Body>
</soapenv:Envelope>
                ");
                if (!response.StatusCode.ToString().Equals("OK"))
                {
                    var resXml = XDocument.Parse(response.Content);
                    var error = resXml.Descendants(nsSoap + "Body").First().Element(nsSoap + "Fault").Element("faultstring").Value;
                    Log.Error($"CUCM: Error on try remove FacInfo {fac.Name}, message={error}");
                }
                else
                {
                    var resXml = XDocument.Parse(response.Content);
                    var @return = resXml.Descendants(nsSoap + "Body").First().Element(nsCucm + "removeFacInfoResponse").Element("return").Value;
                    Log.Info($"CUCM: Success on remove FacInfo {fac.Name}, return={@return}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CUCM: Error on try remove FacInfo {fac.Name}, message={ex.Message}");
            }
        }

        IRestResponse CiscoAXL(string body)
        {
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            var client = new RestClient($"https://{appSettings["host"]}:{appSettings["port"]}/axl/");
            client.Timeout = -1;
            var request = new RestRequest(Method.POST);
            request.AddHeader("Content-Type", "text/plain");
            request.AddHeader("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"{appSettings["login"]}:{appSettings["senha"]}")));
            request.AddParameter("text/plain", body, ParameterType.RequestBody);
            IRestResponse response = client.Execute(request);
            if (response.ErrorException != null)
            {
                throw new Exception(response.ErrorMessage);
            }
            return response;
        }

        void GetFiles()
        {
            using (Session session = new Session())
            {
                try
                {
                    session.Open(sessionOptions);
                    IEnumerable<RemoteFileInfo> files = session.EnumerateRemoteFiles(appSettings["pasta_arquivos_ftp"], "*.csv", EnumerationOptions.None);
                    foreach (RemoteFileInfo file in files)
                    {
                        session.GetFiles(file.FullName, $@"temp\{file.Name}", true, new TransferOptions()).Check();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"FTP: {ex.Message}");
                }
            }
        }

        void PutFiles()
        {
            DirectoryInfo directoryInfo = new DirectoryInfo("temp");
            FileInfo[] files = directoryInfo.GetFiles("*.csv");

            using (Session session = new Session())
            {
                try
                {
                    session.Open(sessionOptions);

                    foreach (FileInfo file in files)
                    {
                        TransferOperationResult transferOperationResult = session.PutFiles($@"temp\{file.Name}", $"{appSettings["pasta_realizados_ftp"]}/{file.Name}", true, null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"FTP: {ex.Message}");
                }
            }
        }
    }

    class Rlt
    {
        public virtual string Acao { get; set; }
        public virtual string Matricula { get; set; }
        public virtual string Subcentral { get; set; }
        public virtual string Categoria { get; set; }
        public virtual string Senha { get; set; }
        public virtual string RamalVirtual { get; set; }
    }

    class Fac
    {
        public virtual string Name { get; set; }
        public virtual string Code { get; set; }
        public virtual string AuthorizationLevel { get; set; }
    }
}
