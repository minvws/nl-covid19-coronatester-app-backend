﻿// // Copyright 2021 De Staat der Nederlanden, Ministerie van Volksgezondheid, Welzijn en Sport.
// // Licensed under the EUROPEAN UNION PUBLIC LICENCE v. 1.2
// // SPDX-License-Identifier: EUPL-1.2

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NL.Rijksoverheid.CoronaCheck.BackEnd.Common.Certificates;
using NL.Rijksoverheid.CoronaCheck.BackEnd.Common.Extensions;
using NL.Rijksoverheid.CoronaCheck.BackEnd.Common.Services;
using NL.Rijksoverheid.CoronaCheck.BackEnd.Common.Signing;

namespace NL.Rijksoverheid.CoronaCheck.BackEnd.DigitalGreenGatewayTool.Client
{
    public class DgcgClient : IDgcgClient
    {
        private readonly IAuthenticationCertificateProvider _authCertProvider;
        private readonly IDgcgClientConfig _config;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IContentSigner _signer;

        public DgcgClient(IDgcgClientConfig config, IJsonSerializer jsonSerializer, IAuthenticationCertificateProvider authCertProvider, IContentSigner signer)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _authCertProvider = authCertProvider ?? throw new ArgumentNullException(nameof(authCertProvider));
            _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        }

        public async Task<IReadOnlyList<TrustListDto>> GetTrustList()
        {
            var uri = new Uri($"{_config.GatewayUrl}/trustList");

            var response = await ExecuteRequest(uri);

            return _jsonSerializer.Deserialize<List<TrustListDto>>(response);
        }

        public async Task<IReadOnlyList<TrustListDto>> GetTrustList(CertificateType type)
        {
            var uri = new Uri($"{_config.GatewayUrl}/trustList/{type.ToString().ToUpper()}");

            var response = await ExecuteRequest(uri);

            return _jsonSerializer.Deserialize<List<TrustListDto>>(response);
        }

        public async Task<IReadOnlyList<TrustListDto>> GetTrustList(CertificateType type, string country)
        {
            var uri = new Uri($"{_config.GatewayUrl}/trustList/{type.ToString().ToUpper()}/{country}");

            var response = await ExecuteRequest(uri);

            return _jsonSerializer.Deserialize<List<TrustListDto>>(response);
        }

        public async Task<bool> Upload(byte[] certificateBytes)
        {
            var uri = new Uri($"{_config.GatewayUrl}/signerCertificate");

            Console.WriteLine($"Initializing Upload call to {uri.AbsoluteUri}");

            // Configure authentication certificate
            using var clientCert = _authCertProvider.GetCertificate();
            using var clientHandler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual
            };
            clientHandler.ClientCertificates.Clear();
            clientHandler.ClientCertificates.Add(clientCert);

            var request = new HttpRequestMessage {RequestUri = uri, Method = HttpMethod.Post};

            // Set the auth headers
            if (_config.SendAuthenticationHeaders)
            {
                Console.WriteLine($"SendAuthenticationHeaders enabled, sending them with the request.");
                var sha = clientCert.ComputeSha256Hash();
                request.Headers.Add("X-SSL-Client-SHA256", sha);
                var dn = clientCert.Subject.Replace(" ", string.Empty);
                request.Headers.Add("X-SSL-Client-DN", dn);
            } else
            {
                Console.WriteLine($"SendAuthenticationHeaders disabled, not sending them with the request.");
            }

            Console.WriteLine($"Signing: IncludeChain={_config.IncludeChainInSignature}, IncludeCertificates={_config.IncludeCertsInSignature}");

            // Sign the cert
            var signatureBytes = _signer.GetSignature(certificateBytes, _config.IncludeChainInSignature, !_config.IncludeCertsInSignature, false);

            // Generate the body
            var requestContentString = Convert.ToBase64String(signatureBytes);
            request.Content = new StringContent(requestContentString, Encoding.UTF8, "application/cms");

            Console.WriteLine("Sending the following request:");
            Console.WriteLine(request.ToString());
            Console.WriteLine("Request content string:");
            Console.WriteLine(requestContentString);

            using var client = new HttpClient(clientHandler);
            var response = await client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Created) return true;

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                Console.WriteLine($"ERROR: a certificate of type TODO already exists!");
            }

            Console.WriteLine($"ERROR: HTTP status code: {response.StatusCode}; received the following response body:");
            Console.WriteLine($"{await response.Content.ReadAsStringAsync()}");

            return false;
        }

        public async Task<bool> Revoke(byte[] certificateBytes)
        {
            var uri = new Uri($"{_config.GatewayUrl}/signerCertificate");

            Console.WriteLine($"Initializing Revoke call to {uri.AbsoluteUri}");

            // Configure authentication certificate
            using var clientCert = _authCertProvider.GetCertificate();
            using var clientHandler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual
            };
            clientHandler.ClientCertificates.Clear();
            clientHandler.ClientCertificates.Add(clientCert);

            var request = new HttpRequestMessage {RequestUri = uri, Method = HttpMethod.Delete};

            // Set the auth headers
            if (_config.SendAuthenticationHeaders)
            {
                Console.WriteLine($"SendAuthenticationHeaders enabled, sending them with the request.");
                var sha = clientCert.ComputeSha256Hash();
                request.Headers.Add("X-SSL-Client-SHA256", sha);
                var dn = clientCert.Subject.Replace(" ", string.Empty);
                request.Headers.Add("X-SSL-Client-DN", dn);
            }
            else
            {
                Console.WriteLine($"SendAuthenticationHeaders disabled, not sending them with the request.");
            }

            Console.WriteLine($"Signing: IncludeChain={_config.IncludeChainInSignature}, IncludeCertificates={_config.IncludeCertsInSignature}");

            // Sign the cert
            var signatureBytes = _signer.GetSignature(certificateBytes, _config.IncludeChainInSignature, !_config.IncludeCertsInSignature, false);

            // Generate the body
            var requestContentString = Convert.ToBase64String(signatureBytes);
            request.Content = new StringContent(requestContentString, Encoding.UTF8, "application/cms");

            Console.WriteLine("Sending the following request:");
            Console.WriteLine(request.ToString());
            Console.WriteLine("Request content string:");
            Console.WriteLine(requestContentString);

            using var client = new HttpClient(clientHandler);
            var response = await client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NoContent) return true;

            Console.WriteLine($"ERROR: HTTP status code: {response.StatusCode}; received the following response body:");
            Console.WriteLine($"{await response.Content.ReadAsStringAsync()}");

            return false;
        }

        private async Task<string> ExecuteRequest(Uri uri)
        {
            Console.WriteLine($"Initializing trustList call to {uri.AbsoluteUri}");

            // Configure authentication certificate
            using var clientCert = _authCertProvider.GetCertificate();
            using var clientHandler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual
            };
            clientHandler.ClientCertificates.Clear();
            clientHandler.ClientCertificates.Add(clientCert);

            var request = new HttpRequestMessage {RequestUri = uri, Method = HttpMethod.Get};

            // Set the auth headers
            if (_config.SendAuthenticationHeaders)
            {
                Console.WriteLine($"SendAuthenticationHeaders enabled, sending them with the request.");
                var sha = clientCert.ComputeSha256Hash();
                request.Headers.Add("X-SSL-Client-SHA256", sha);
                var dn = clientCert.Subject.Replace(" ", string.Empty);
                request.Headers.Add("X-SSL-Client-DN", dn);
            }
            else
            {
                Console.WriteLine($"SendAuthenticationHeaders disabled, not sending them with the request.");
            }

            // Set other required headers
            request.Headers.Add("accept", "application/json");

            Console.WriteLine("Sending the following request:");
            Console.WriteLine(request.ToString());

            using var client = new HttpClient(clientHandler);
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();

            Console.WriteLine($"ERROR: HTTP status code: {response.StatusCode}; received the following response body:");
            Console.WriteLine($"{await response.Content.ReadAsStringAsync()}");

            var msg = $"Something went wrong. HTTP response code: {response.StatusCode}; response body: {await response.Content.ReadAsStringAsync()}";

            throw new Exception(msg);
        }
    }
}
