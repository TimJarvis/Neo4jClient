﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using Neo4jClient.Deserializer;
using Neo4jClient.Serializer;
using Newtonsoft.Json;
using RestSharp;

namespace Neo4jClient
{
    public class GraphClient : IGraphClient
    {
        readonly Uri rootUri;
        readonly IHttpFactory httpFactory;
        internal RootApiResponse RootApiResponse;

        public NullValueHandling JsonSerializerNullValueHandling { get; set; }

        public GraphClient(Uri rootUri)
            : this(rootUri, new Http())
        {
        }

        public GraphClient(Uri rootUri, IHttpFactory httpFactory)
        {
            this.rootUri = rootUri;
            this.httpFactory = httpFactory;
            JsonSerializerNullValueHandling = NullValueHandling.Ignore;
        }

        IRestClient CreateClient()
        {
            var client = new RestClient(rootUri.AbsoluteUri) {HttpFactory = httpFactory};
            client.RemoveHandler("application/json");
            client.AddHandler("application/json", new CustomJsonDeserializer());
            return client;
        }

        public virtual void Connect()
        {
            var request = new RestRequest("", Method.GET);
            var response = CreateClient().Execute<RootApiResponse>(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.OK);

            RootApiResponse = response.Data;
            RootApiResponse.Node = RootApiResponse.Node.Substring(rootUri.AbsoluteUri.Length);
            RootApiResponse.NodeIndex = RootApiResponse.NodeIndex.Substring(rootUri.AbsoluteUri.Length);
            RootApiResponse.RelationshipIndex = RootApiResponse.RelationshipIndex.Substring(rootUri.AbsoluteUri.Length);
            RootApiResponse.ReferenceNode = RootApiResponse.ReferenceNode.Substring(rootUri.AbsoluteUri.Length);
            RootApiResponse.ExtensionsInfo = RootApiResponse.ExtensionsInfo.Substring(rootUri.AbsoluteUri.Length);
            if (RootApiResponse.Extensions != null && RootApiResponse.Extensions.GremlinPlugin != null)
            {
                RootApiResponse.Extensions.GremlinPlugin.ExecuteScript =
                    RootApiResponse.Extensions.GremlinPlugin.ExecuteScript.Substring(rootUri.AbsoluteUri.Length);
            }
        }

        public virtual RootNode RootNode
        {
            get { return new RootNode(this); }
        }

        public virtual NodeReference<TNode> Create<TNode>(TNode node,
                                                          IEnumerable<IRelationshipAllowingParticipantNode<TNode>>
                                                              relationships, IEnumerable<IndexEntry> indexEntries)
            where TNode : class
        {
            if (node == null)
                throw new ArgumentNullException("node");

            var validationContext = new ValidationContext(node, null, null);
            Validator.ValidateObject(node, validationContext);

            var calculatedRelationships = relationships
                .Cast<Relationship>()
                .Select(r => new
                    {
                        CalculatedDirection = Relationship.DetermineRelationshipDirection(typeof (TNode), r),
                        Relationship = r
                    })
                .ToArray();

            CheckRoot();

            var request = new RestRequest(RootApiResponse.Node, Method.POST)
                {
                    RequestFormat = DataFormat.Json,
                    JsonSerializer = new CustomJsonSerializer {NullHandling = JsonSerializerNullValueHandling}
                };
            request.AddBody(node);
            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.Created);
            
            var nodeLocation = response.Headers.GetParameter("Location");
            var nodeId = int.Parse(GetLastPathSegment(nodeLocation));
            var nodeReference = new NodeReference<TNode>(nodeId, this);

            foreach (var relationship in calculatedRelationships)
            {
                var participants = new[] {nodeReference, relationship.Relationship.OtherNode};
                NodeReference sourceNode, targetNode;
                switch (relationship.CalculatedDirection)
                {
                    case RelationshipDirection.Outgoing:
                        sourceNode = participants[0];
                        targetNode = participants[1];
                        break;
                    case RelationshipDirection.Incoming:
                        sourceNode = participants[1];
                        targetNode = participants[0];
                        break;
                    default:
                        throw new NotSupportedException(string.Format(
                            "The specified relationship direction is not supported: {0}",
                            relationship.CalculatedDirection));
                }

                CreateRelationship(
                    sourceNode,
                    targetNode,
                    relationship.Relationship.RelationshipTypeKey,
                    relationship.Relationship.Data);
            }

            ReIndex(nodeReference, indexEntries);

            return nodeReference;
        }

        public virtual void CreateRelationship<TSourceNode, TRelationship>(
            NodeReference<TSourceNode> sourceNodeReference,
            TRelationship relationship)
            where TRelationship :
                Relationship,
                IRelationshipAllowingSourceNode<TSourceNode>
        {
            if (relationship.Direction == RelationshipDirection.Incoming)
                throw new NotSupportedException("Incoming relationships are not yet supported by this method.");

            CreateRelationship(
                sourceNodeReference,
                relationship.OtherNode,
                relationship.RelationshipTypeKey,
                relationship.Data);
        }

        void CreateRelationship(NodeReference sourceNode, NodeReference targetNode, string relationshipTypeKey,
                                object data)
        {
            var relationship = new RelationshipTemplate
                {
                    To = rootUri + ResolveEndpoint(targetNode),
                    Data = data,
                    Type = relationshipTypeKey
                };

            var sourceNodeEndpoint = ResolveEndpoint(sourceNode) + "/relationships";
            var request = new RestRequest(sourceNodeEndpoint, Method.POST)
                {
                    RequestFormat = DataFormat.Json,
                    JsonSerializer = new CustomJsonSerializer {NullHandling = JsonSerializerNullValueHandling}
                };
            request.AddBody(relationship);
            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.Created, HttpStatusCode.NotFound);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ApplicationException(string.Format(
                    "One of the nodes referenced in the relationship could not be found. Referenced nodes were {0} and {1}.",
                    sourceNode.Id,
                    targetNode.Id));
        }

        public void DeleteRelationship(RelationshipReference reference)
        {
            CheckRoot();

            var relationshipEndpoint = ResolveEndpoint(reference);
            var request = new RestRequest(relationshipEndpoint, Method.DELETE);
            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.NoContent, HttpStatusCode.NotFound);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ApplicationException(string.Format(
                    "Unable to delete the relationship. The response status was: {0} {1}",
                    (int) response.StatusCode,
                    response.StatusDescription));
        }

        public virtual Node<TNode> Get<TNode>(NodeReference reference)
        {
            CheckRoot();

            var nodeEndpoint = ResolveEndpoint(reference);
            var request = new RestRequest(nodeEndpoint, Method.GET);
            var response = CreateClient().Execute<NodeApiResponse<TNode>>(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.OK, HttpStatusCode.NotFound);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            return response.Data.ToNode(this);
        }

        public virtual Node<TNode> Get<TNode>(NodeReference<TNode> reference)
        {
            return Get<TNode>((NodeReference) reference);
        }

        public void Update<TNode>(NodeReference<TNode> nodeReference, Action<TNode> updateCallback)
        {
            CheckRoot();

            var node = Get(nodeReference);
            updateCallback(node.Data);

            var nodeEndpoint = ResolveEndpoint(nodeReference);
            var request = new RestRequest(nodeEndpoint + "/properties", Method.PUT)
                {
                    RequestFormat = DataFormat.Json,
                    JsonSerializer = new CustomJsonSerializer {NullHandling = JsonSerializerNullValueHandling}
                };
            request.AddBody(node.Data);
            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.NoContent);
        }

        public virtual void Delete(NodeReference reference, DeleteMode mode)
        {
            CheckRoot();

            if (mode == DeleteMode.NodeAndRelationships)
            {
                DeleteAllRelationships(reference);
            }

            var nodeEndpoint = ResolveEndpoint(reference);
            var request = new RestRequest(nodeEndpoint, Method.DELETE);
            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.NoContent, HttpStatusCode.Conflict);

            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new ApplicationException(string.Format(
                    "Unable to delete the node. The node may still have relationships. The response status was: {0} {1}",
                    (int) response.StatusCode,
                    response.StatusDescription));
        }

        void DeleteAllRelationships(NodeReference reference)
        {
            //TODO: Make this a dynamic endpoint resolution
            var relationshipsEndpoint = ResolveEndpoint(reference) + "/relationships/all";
            var request = new RestRequest(relationshipsEndpoint, Method.GET);
            var response = CreateClient().Execute<List<RelationshipApiResponse>>(request);

            var relationshipResources = response
                .Data
                .Select(r => r.Self.Substring(rootUri.AbsoluteUri.Length));

            foreach (var relationshipResource in relationshipResources)
            {
                request = new RestRequest(relationshipResource, Method.DELETE);
                CreateClient().Execute(request);
            }
        }

        string ResolveEndpoint(NodeReference node)
        {
            return RootApiResponse.Node + "/" + node.Id;
        }

        string ResolveEndpoint(RelationshipReference relationship)
        {
            //TODO: Make this a dynamic endpoint resolution
            return "relationship/" + relationship.Id;
        }

        static string GetLastPathSegment(string uri)
        {
            var path = new Uri(uri).AbsolutePath;
            return path
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .LastOrDefault();
        }

        public virtual string ExecuteScalarGremlin(string query, IDictionary<string, object> parameters = null)
        {
            CheckRoot();

            var request = new RestRequest(RootApiResponse.Extensions.GremlinPlugin.ExecuteScript, Method.POST)
            {
                RequestFormat = DataFormat.Json,
                JsonSerializer = new CustomJsonSerializer { NullHandling = JsonSerializerNullValueHandling }
            };
            request.AddBody(new GremlinApiQuery(query, parameters));
            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(
                response,
                string.Format("The query was: {0}", query),
                HttpStatusCode.OK);

            return response.Content;
        }

        public virtual IEnumerable<Node<TNode>> ExecuteGetAllNodesGremlin<TNode>(string query, IDictionary<string, object> parameters = null)
        {
            CheckRoot();

            var request = new RestRequest(RootApiResponse.Extensions.GremlinPlugin.ExecuteScript, Method.POST)
            {
                RequestFormat = DataFormat.Json,
                JsonSerializer = new CustomJsonSerializer { NullHandling = JsonSerializerNullValueHandling }
            };
            request.AddBody(new GremlinApiQuery(query, parameters));
            var response = CreateClient().Execute<List<NodeApiResponse<TNode>>>(request);

            ValidateExpectedResponseCodes(
                response,
                string.Format("The query was: {0}", query),
                HttpStatusCode.OK);

            return response.Data == null
                ? Enumerable.Empty<Node<TNode>>()
                : response.Data.Select(r => r.ToNode(this));
        }

        public virtual IEnumerable<RelationshipInstance> ExecuteGetAllRelationshipsGremlin(string query, IDictionary<string, object> parameters = null)
        {
            CheckRoot();

            var request = new RestRequest(RootApiResponse.Extensions.GremlinPlugin.ExecuteScript, Method.POST)
            {
                RequestFormat = DataFormat.Json,
                JsonSerializer = new CustomJsonSerializer { NullHandling = JsonSerializerNullValueHandling }
            };
            request.AddBody(new GremlinApiQuery(query, parameters));
            var response = CreateClient().Execute<List<RelationshipApiResponse>>(request);

            ValidateExpectedResponseCodes(
                response,
                string.Format("The query was: {0}", query),
                HttpStatusCode.OK);

            return response.Data == null
                ? Enumerable.Empty<RelationshipInstance>()
                : response.Data.Select(r => r.ToRelationshipInstance(this));
        }

        public Dictionary<string, IndexMetaData> GetIndexes(IndexFor indexFor)
        {
            CheckRoot();

            string indexResource;
            switch (indexFor)
            {
                case IndexFor.Node:
                    indexResource = RootApiResponse.NodeIndex;
                    break;
                case IndexFor.Relationship:
                    indexResource = RootApiResponse.RelationshipIndex;
                    break;
                default:
                    throw new NotSupportedException(string.Format("GetIndexes does not support indexfor {0}", indexFor));
            }

            var request = new RestRequest(indexResource, Method.GET)
            {
                RequestFormat = DataFormat.Json,
                JsonSerializer = new CustomJsonSerializer { NullHandling = JsonSerializerNullValueHandling }
            };

            var response =  CreateClient().Execute<Dictionary<string, IndexMetaData>>(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.OK);

            return response.Data;
        }

        public bool CheckIndexExists(string indexName, IndexFor indexFor)
        {
            CheckRoot();

            string indexResource;
            switch (indexFor)
            {
                case IndexFor.Node:
                    indexResource = RootApiResponse.NodeIndex;
                    break;
                case IndexFor.Relationship:
                    indexResource = RootApiResponse.RelationshipIndex;
                    break;
                default:
                    throw new NotSupportedException(string.Format("IndexExists does not support indexfor {0}", indexFor));
            }

            var request = new RestRequest(string.Format("{0}/{1}",indexResource, indexName), Method.GET)
            {
                RequestFormat = DataFormat.Json,
                JsonSerializer = new CustomJsonSerializer { NullHandling = JsonSerializerNullValueHandling }
            };

            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.OK, HttpStatusCode.NotFound);

            return response.StatusCode == HttpStatusCode.OK;
        }

        void CheckRoot()
        {
            if (RootApiResponse == null)
                throw new InvalidOperationException(
                    "The graph client is not connected to the server. Call the Connect method first.");
        }

        public void CreateIndex(string indexName, IndexConfiguration config, IndexFor indexFor)
        {
            CheckRoot();

            string nodeResource;
            switch (indexFor)
            {
                case IndexFor.Node:
                    nodeResource = RootApiResponse.NodeIndex;
                    break;
                case IndexFor.Relationship:
                    nodeResource = RootApiResponse.RelationshipIndex;
                    break;
                default:
                    throw new NotSupportedException(string.Format("CreateIndex does not support indexfor {0}", indexFor));
            }

            var createIndexApiRequest = new
                {
                    name = indexName.ToLower(),
                    config
                };

            var request = new RestRequest(nodeResource, Method.POST)
                {
                    RequestFormat = DataFormat.Json,
                    JsonSerializer = new CustomJsonSerializer {NullHandling = JsonSerializerNullValueHandling}
                };
            request.AddBody(createIndexApiRequest);

            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.Created);
        }

        public void ReIndex(NodeReference node, IEnumerable<IndexEntry> indexEntries)
        {
            if (indexEntries == null)
                throw new ArgumentNullException("indexEntries");

            CheckRoot();

            var nodeAddress = string.Join("/", new[] {RootApiResponse.Node, node.Id.ToString()});

            var updates = indexEntries
                .SelectMany(
                    i => i.KeyValues,
                    (i, kv) => new {IndexName = i.Name, kv.Key, kv.Value})
                .Where(update => update.Value != null);

            foreach (var update in updates)
            {
                string indexValue;
                if(update.Value is DateTimeOffset)
                {
                    indexValue = ((DateTimeOffset) update.Value).UtcTicks.ToString();
                }
                else if (update.Value is DateTime)
                {
                    indexValue = ((DateTime)update.Value).Ticks.ToString();
                }
                else
                {
                    indexValue = update.Value.ToString();
                }

                if (string.IsNullOrWhiteSpace(indexValue))
                    continue;

                AddNodeToIndex(update.IndexName, update.Key, indexValue, nodeAddress);
            }
        }

        public void DeleteIndex(string indexName, IndexFor indexFor)
        {
            CheckRoot();

            string indexResource;
            switch (indexFor)
            {
                case IndexFor.Node:
                    indexResource = RootApiResponse.NodeIndex;
                    break;
                case IndexFor.Relationship:
                    indexResource = RootApiResponse.RelationshipIndex;
                    break;
                default:
                    throw new NotSupportedException(string.Format("DeleteIndex does not support indexfor {0}", indexFor));
            }

            var request = new RestRequest(string.Format("{0}/{1}", indexResource, indexName), Method.DELETE)
            {
                RequestFormat = DataFormat.Json,
                JsonSerializer = new CustomJsonSerializer { NullHandling = JsonSerializerNullValueHandling }
            };

            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.NoContent);
        }

        void AddNodeToIndex(string indexName, string indexKey, string indexValue, string nodeAddress)
        {
            indexValue = indexValue.Replace('/', '-');

            var nodeIndexAddress = string.Join("/", new[]
                {
                    RootApiResponse.NodeIndex,
                    Uri.EscapeDataString(indexName),
                    Uri.EscapeDataString(indexKey),
                    Uri.EscapeDataString(indexValue)
                });
            var request = new RestRequest(nodeIndexAddress, Method.POST)
            {
                RequestFormat = DataFormat.Json,
                JsonSerializer = new CustomJsonSerializer { NullHandling = JsonSerializerNullValueHandling }
            };
            request.AddBody(string.Join("", rootUri, nodeAddress));

            var response = CreateClient().Execute(request);

            ValidateExpectedResponseCodes(
                response,
                string.Format(
                    "Adding '{0}'='{1}' to index {2} for {3} by POSTing to {4}.",
                    indexKey,
                    indexValue,
                    indexName,
                    nodeAddress,
                    nodeIndexAddress
                ),
                HttpStatusCode.Created);
        }

        public IEnumerable<Node<TNode>> QueryIndex<TNode>(string indexName, IndexFor indexFor, string query)
        {
            CheckRoot();

            string indexResource;

            switch (indexFor)
            {
                case IndexFor.Node:
                    indexResource = RootApiResponse.NodeIndex;
                    break;
                case IndexFor.Relationship:
                    indexResource = RootApiResponse.RelationshipIndex;
                    break;
                default:
                    throw new NotSupportedException(string.Format("QueryIndex does not support indexfor {0}", indexFor));
            }

            var request = new RestRequest(indexResource + "/" + indexName, Method.GET)
                {
                    RequestFormat = DataFormat.Json,
                    JsonSerializer = new CustomJsonSerializer {NullHandling = JsonSerializerNullValueHandling}
                };

            request.AddParameter("query", query);

            var response = CreateClient().Execute<List<NodeApiResponse<TNode>>>(request);

            ValidateExpectedResponseCodes(response, HttpStatusCode.OK);

            return response.Data == null
                ? Enumerable.Empty<Node<TNode>>()
                : response.Data.Select(r => r.ToNode(this));
        }

        static void ValidateExpectedResponseCodes(RestResponseBase response, params HttpStatusCode[] allowedStatusCodes)
        {
            ValidateExpectedResponseCodes(response, null, allowedStatusCodes);
        }

        static void ValidateExpectedResponseCodes(RestResponseBase response, string commandDescription, params HttpStatusCode[] allowedStatusCodes)
        {
            commandDescription = string.IsNullOrWhiteSpace(commandDescription)
                ? ""
                : commandDescription + "\r\n\r\n";

            if (response.ErrorException != null)
                throw new ApplicationException(string.Format(
                    "Received an exception when executing the request.\r\n\r\n{0}The exception was: {1} {2}",
                    commandDescription,
                    response.ErrorMessage,
                    response.ErrorException));

            if (!allowedStatusCodes.Contains(response.StatusCode))
                throw new ApplicationException(string.Format(
                    "Received an unexpected HTTP status when executing the request.\r\n\r\n{0}The response status was: {1} {2}",
                    commandDescription,
                    (int) response.StatusCode,
                    response.StatusDescription));
        }
    }
}
