﻿/*
 * Minimal Object Storage Library, (C) 2015 Minio, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RestSharp;
using System.IO;
using Minio.Client.xml;
using System.Xml.Serialization;
using System.Security.Cryptography;

namespace Minio.Client
{
    public class ObjectStorageClient
    {
        private static int PART_SIZE = 5 * 1024 * 1024;

        private RestClient client;
        private string region;

        internal ObjectStorageClient(Uri uri, string accessKey, string secretKey)
        {
            this.client = new RestClient(uri);
            this.client.UserAgent =  "minio-cs/0.0.1 (Windows 8.1; x86_64)";
            this.region = "us-west-2";
            if (accessKey != null && secretKey != null)
            {
                this.client.Authenticator = new V4Authenticator(accessKey, secretKey);
            }
        }
        public static ObjectStorageClient GetClient(Uri uri)
        {
            return GetClient(uri, null, null);
        }
        public static ObjectStorageClient GetClient(Uri uri, string accessKey, string secretKey)
        {
            if (uri == null)
            {
                throw new NullReferenceException();
            }

            if (!(uri.Scheme == "http" || uri.Scheme == "https"))
            {
                throw new UriFormatException("Expecting http or https");
            }

            if (uri.Query.Length != 0)
            {
                throw new UriFormatException("Expecting no query");
            }

            if (uri.AbsolutePath.Length == 0 || (uri.AbsolutePath.Length == 1 && uri.AbsolutePath[0] == '/'))
            {
                String path = uri.Scheme + "://" + uri.Host + ":" + uri.Port + "/";
                return new ObjectStorageClient(new Uri(path), accessKey, secretKey);
            }
            throw new UriFormatException("Expecting AbsolutePath to be empty");
        }

        public static ObjectStorageClient GetClient(string url)
        {
            return GetClient(url, null, null);
        }

        public static ObjectStorageClient GetClient(string url, string accessKey, string secretKey)
        {
            Uri uri = new Uri(url);
            return GetClient(uri, accessKey, secretKey);
        }

        public bool BucketExists(string bucket)
        {
            var request = new RestRequest(bucket, Method.HEAD);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return true;
            }
            return false;
        }

        public void MakeBucket(string bucket, Acl acl)
        {
            var request = new RestRequest("/" + bucket, Method.PUT);

            CreateBucketConfiguration config = new CreateBucketConfiguration()
            {
                LocationConstraint = this.region
            };

            request.AddBody(config);

            var response = client.Execute(request);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return;
            }

            throw ParseError(response);
        }

        public void MakeBucket(string bucket)
        {
            this.MakeBucket(bucket, Acl.Private);
        }

        public void RemoveBucket(string bucket)
        {
            var request = new RestRequest(bucket, Method.DELETE);
            client.Execute(request);
        }

        public void GetBucketAcl(string bucket)
        {
            var request = new RestRequest(bucket + "?acl", Method.GET);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                // TODO parse
            }
            // TODO work out what error to throw
            throw new NotImplementedException();
        }

        public void SetBucketAcl(string bucket, Acl acl)
        {
            var request = new RestRequest(bucket + "?acl", Method.PUT);
            // TODO add acl header
            request.AddHeader("x-amz-acl", acl.ToString());
            var response = client.Execute(request);
        }

        public ListAllMyBucketsResult ListBuckets()
        {
            var request = new RestRequest("/", Method.GET);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var contentBytes = System.Text.Encoding.UTF8.GetBytes(response.Content);
                var stream = new MemoryStream(contentBytes);
                ListAllMyBucketsResult bucketList = (ListAllMyBucketsResult)(new XmlSerializer(typeof(ListAllMyBucketsResult)).Deserialize(stream));
                return bucketList;
            }
            throw ParseError(response);
        }

        public Stream GetObject(string bucket, string key)
        {
            MemoryStream stream = new MemoryStream();
            var request = new RestRequest(bucket + "/ " + key, Method.GET);
            request.ResponseWriter(stream);
            var response = client.Execute(request);
            return stream;
        }

        public Stream GetObject(string bucket, string key, UInt64 offset)
        {
            MemoryStream stream = new MemoryStream();
            var request = new RestRequest(bucket + "/ " + key, Method.GET);
            request.AddHeader("Range", "bytes=" + offset + "-");
            request.ResponseWriter(stream);
            var response = client.Execute(request);
            return stream;
        }

        public Stream GetObject(string bucket, string key, UInt64 offset, UInt64 length)
        {
            MemoryStream stream = new MemoryStream();
            var request = new RestRequest(bucket + "/ " + key, Method.GET);
            request.AddHeader("Range", "bytes=" + offset + "-" + (offset + length - 1));
            request.ResponseWriter(stream);
            var response = client.Execute(request);
            return stream;
        }

        public ObjectStat StatObject(string bucket, string key)
        {
            var request = new RestRequest(bucket + "/" + key, Method.HEAD);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                UInt64 size = 0;
                DateTime lastModified = new DateTime();
                string etag = "";
                foreach (Parameter parameter in response.Headers) {
                    if(parameter.Name == "Content-Length") {
                        size = UInt64.Parse(parameter.Value.ToString());
                    }
                    if (parameter.Name == "Last-Modified")
                    {
                        // TODO parse datetime
                        lastModified = new DateTime();
                    }
                    if (parameter.Name == "ETag")
                    {
                        etag = parameter.Value.ToString();
                    }
                }

                return new ObjectStat(key, size, lastModified, etag);
            }
            throw new NotImplementedException();
        }

        private RequestException ParseError(IRestResponse response)
        {
            var contentBytes = System.Text.Encoding.UTF8.GetBytes(response.Content);
            var stream = new MemoryStream(contentBytes);
            ErrorResponse errorResponse = (ErrorResponse)(new XmlSerializer(typeof(ErrorResponse)).Deserialize(stream));
            return new RequestException()
            {
                Response = errorResponse,
                XmlError = response.Content
            };
        }

    }
}
