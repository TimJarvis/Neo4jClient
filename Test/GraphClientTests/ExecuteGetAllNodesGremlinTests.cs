﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using NUnit.Framework;
using System.Linq;
using RestSharp;

namespace Neo4jClient.Test.GraphClientTests
{
    [TestFixture]
    public class ExecuteGetAllNodesGremlinTests
    {
        [Test]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ShouldThrowInvalidOperationExceptionIfNotConnected()
        {
            var client = new GraphClient(new Uri("http://foo"));
            client.ExecuteGetAllNodesGremlin<object>("", null);
        }

        public class Foo
        {
            public string Bar { get; set; }
            public string Baz { get; set; }
        }

        [Test]
        public void ShouldReturnIEnumerableOfObjects()
        {
            //Arrange
            const string gremlinQueryExpected = "foo bar query";

            var httpFactory = MockHttpFactory.Generate("http://foo/db/data", new Dictionary<RestRequest, HttpResponse>
            {
                {
                    new RestRequest { Resource = "/", Method = Method.GET },
                    new HttpResponse
                    {
                        StatusCode = HttpStatusCode.OK,
                        ContentType = "application/json",
                        Content = @"{
                          'node' : 'http://foo/db/data/node',
                          'node_index' : 'http://foo/db/data/index/node',
                          'relationship_index' : 'http://foo/db/data/index/relationship',
                          'reference_node' : 'http://foo/db/data/node/0',
                          'extensions_info' : 'http://foo/db/data/ext',
                          'extensions' : {
                            'GremlinPlugin' : {
                              'execute_script' : 'http://foo/db/data/ext/GremlinPlugin/graphdb/execute_script'
                            }
                          }
                        }".Replace('\'', '"')
                    }
                },
                {
                    new RestRequest {
                        Resource = "/ext/GremlinPlugin/graphdb/execute_script",
                        Method = Method.POST,
                        RequestFormat = DataFormat.Json
                    }.AddParameter("script", gremlinQueryExpected, ParameterType.GetOrPost),
                    new HttpResponse {
                        StatusCode = HttpStatusCode.OK,
                        ContentType = "application/json",
                        Content =@"[ {
                            'outgoing_relationships' : 'http://foo/db/data/node/5/relationships/out',
                            'data' : {
                                'Bar' : 'bar',
                                'Baz' : 'baz'
                            },
                            'traverse' : 'http://foo/db/data/node/5/traverse/{returnType}',
                            'all_typed_relationships' : 'http://foo/db/data/node/5/relationships/all/{-list|&|types}',
                            'property' : 'http://foo/db/data/node/5/properties/{key}',
                            'self' : 'http://foo/db/data/node/5',
                            'properties' : 'http://foo/db/data/node/5/properties',
                            'outgoing_typed_relationships' : 'http://foo/db/data/node/5/relationships/out/{-list|&|types}',
                            'incoming_relationships' : 'http://foo/db/data/node/5/relationships/in',
                            'extensions' : {
                            },
                            'create_relationship' : 'http://foo/db/data/node/5/relationships',
                            'all_relationships' : 'http://foo/db/data/node/5/relationships/all',
                            'incoming_typed_relationships' : 'http://foo/db/data/node/5/relationships/in/{-list|&|types}'
                        }, {
                            'outgoing_relationships' : 'http://foo/db/data/node/6/relationships/out',
                            'data' : {
                                'Bar' : '123',
                                'Baz' : '456'
                            },
                            'traverse' : 'http://foo/db/data/node/6/traverse/{returnType}',
                            'all_typed_relationships' : 'http://foo/db/data/node/6/relationships/all/{-list|&|types}',
                            'property' : 'http://foo/db/data/node/6/properties/{key}',
                            'self' : 'http://foo/db/data/node/6',
                            'properties' : 'http://foo/db/data/node/6/properties',
                            'outgoing_typed_relationships' : 'http://foo/db/data/node/6/relationships/out/{-list|&|types}',
                            'incoming_relationships' : 'http://foo/db/data/node/6/relationships/in',
                            'extensions' : {
                            },
                            'create_relationship' : 'http://foo/db/data/node/6/relationships',
                            'all_relationships' : 'http://foo/db/data/node/6/relationships/all',
                            'incoming_typed_relationships' : 'http://foo/db/data/node/6/relationships/in/{-list|&|types}'
                        } ]"
                    }
                }
            });
            var graphClient = new GraphClient(new Uri("http://foo/db/data"), httpFactory);
            graphClient.Connect();

            //Act
            var nodes = graphClient
                .ExecuteGetAllNodesGremlin<Foo>(gremlinQueryExpected, new NameValueCollection())
                .ToList();

            //Assert
            Assert.AreEqual(2, nodes.Count());
            Assert.AreEqual("bar", nodes.ElementAt(0).Bar);
            Assert.AreEqual("baz", nodes.ElementAt(0).Baz);
            Assert.AreEqual("123", nodes.ElementAt(1).Bar);
            Assert.AreEqual("456", nodes.ElementAt(1).Baz);
        }

        [Test]
        public void ShouldReturnEmptyEnumerableForNullResult()
        {
            //Arrange
            const string gremlinQueryExpected = "foo bar query";

            var httpFactory = MockHttpFactory.Generate("http://foo/db/data", new Dictionary<RestRequest, HttpResponse>
            {
                {
                    new RestRequest { Resource = "/", Method = Method.GET },
                    new HttpResponse
                    {
                        StatusCode = HttpStatusCode.OK,
                        ContentType = "application/json",
                        Content = @"{
                          'node' : 'http://foo/db/data/node',
                          'node_index' : 'http://foo/db/data/index/node',
                          'relationship_index' : 'http://foo/db/data/index/relationship',
                          'reference_node' : 'http://foo/db/data/node/0',
                          'extensions_info' : 'http://foo/db/data/ext',
                          'extensions' : {
                            'GremlinPlugin' : {
                              'execute_script' : 'http://foo/db/data/ext/GremlinPlugin/graphdb/execute_script'
                            }
                          }
                        }".Replace('\'', '"')
                    }
                },
                {
                    new RestRequest {
                        Resource = "/ext/GremlinPlugin/graphdb/execute_script",
                        Method = Method.POST,
                        RequestFormat = DataFormat.Json
                    }.AddParameter("script", gremlinQueryExpected, ParameterType.GetOrPost),
                    new HttpResponse {
                        StatusCode = HttpStatusCode.OK,
                        ContentType = "application/json",
                        Content =@""
                    }
                }
            });
            var graphClient = new GraphClient(new Uri("http://foo/db/data"), httpFactory);
            graphClient.Connect();

            //Act
            var nodes = graphClient
                .ExecuteGetAllNodesGremlin<NodePacket<string>>(gremlinQueryExpected, new NameValueCollection())
                .ToList();

            //Assert
            Assert.AreEqual(0, nodes.Count());
        }
    }
}