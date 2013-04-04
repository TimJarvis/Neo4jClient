﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using Neo4jClient.Serializer;
using Newtonsoft.Json;

namespace Neo4jClient.Cypher
{
    [DebuggerDisplay("{Query.DebugQueryText}")]
    public partial class CypherFluentQuery :
        ICypherFluentQuery,
        IAttachedReference
    {
        protected readonly IRawGraphClient Client;
        protected readonly QueryWriter QueryWriter;

        public CypherFluentQuery(IGraphClient client)
        {
            if (!(client is IRawGraphClient))
                throw new ArgumentException("The supplied graph client also needs to implement IRawGraphClient", "client");

            Client = (IRawGraphClient)client;
            QueryWriter = new QueryWriter();
        }

        protected CypherFluentQuery(IGraphClient client, QueryWriter queryWriter)
        {
            if (!(client is IRawGraphClient))
                throw new ArgumentException("The supplied graph client also needs to implement IRawGraphClient", "client");

            Client = (IRawGraphClient)client;
            QueryWriter = queryWriter;
        }

        ICypherFluentQuery Mutate(Action<QueryWriter> callback)
        {
            var newWriter = QueryWriter.Clone();
            callback(newWriter);
            return new CypherFluentQuery(Client, newWriter);
        }

        protected ICypherFluentQuery<TResult> Mutate<TResult>(Action<QueryWriter> callback)
        {
            var newWriter = QueryWriter.Clone();
            callback(newWriter);
            return new CypherFluentQuery<TResult>(Client, newWriter);
        }

        public ICypherFluentQuery WithParam(string key, object value)
        {
            if (QueryWriter.ContainsParameterWithKey(key))
                throw new ArgumentException("A parameter with the given key is already defined in the query.", "key");
            return Mutate(w => w.CreateParameter(key, value));
        }

        public ICypherFluentQuery Start(string identity, string startText)
        {
            return Mutate(w =>
                w.AppendClause(string.Format("START {0}={1}", identity, startText)));
        }

        public ICypherFluentQuery Start(object startBits)
        {
            return Mutate(w =>
            {
                var startBitsText = StartBitFormatter.FormatAsCypherText(startBits, w.CreateParameter);
                var startText = "START " + startBitsText;
                w.AppendClause(startText);
            });
        }

        [Obsolete("Use Start(new { foo = nodeRef1, bar = All.Nodes }) instead")]
        public ICypherFluentQuery Start(params ICypherStartBit[] startBits)
        {
            return Mutate(w =>
            {
                var startBitsText = startBits
                    .Select(b => b.ToCypherText(w.CreateParameter))
                    .ToArray();
                var startText = "START " + string.Join(", ", startBitsText);

                w.AppendClause(startText);
            });
        }

        [Obsolete("Use Start(new { foo = new[] { nodeRef1, nodeRef2 } }) instead")]
        public ICypherFluentQuery Start(string identity, params NodeReference[] nodeReferences)
        {
            return Start(new[]
            {
                (ICypherStartBit)(new CypherStartBit(identity, nodeReferences))
            });
        }

        [Obsolete("Use Start(new { foo = new[] { relRef1, relRef2 } }) instead")]
        public ICypherFluentQuery Start(string identity, params RelationshipReference[] relationshipReferences)
        {
            return Start(new CypherStartBit(identity, relationshipReferences));
        }

        public ICypherFluentQuery StartWithNodeIndexLookup(string identity, string indexName, string key, object value)
        {
            return Start(new CypherStartBitWithNodeIndexLookup(identity, indexName, key, value));
        }

        public ICypherFluentQuery StartWithNodeIndexLookup(string identity, string indexName, string parameter)
        {
            return Start(new CypherStartBitWithNodeIndexLookupWithSingleParameter(identity, indexName, parameter));
        }

        [Obsolete("Call Start(new { identity = startText }) instead. Supply multiple parameters as needed, such as Start(new { foo = nodeRef1, bar = nodeRef2 }).", true)]
        public ICypherFluentQuery AddStartPoint(string identity, string startText)
        {
            throw new NotSupportedException();
        }

        [Obsolete("Call Start(new CypherStartBitWithNodeIndexLookup(identity, ...)) instead. Supply multiple parameters as needed, such as Start(new { foo = nodeRef1, bar = nodeRef2 }).", true)]
        public ICypherFluentQuery AddStartPointWithNodeIndexLookup(string identity, string indexName, string key, object value)
        {
            throw new NotSupportedException();
        }

        [Obsolete("Call Start(new { foo = new[] { nodeRef1, nodeRef2 } }) instead. Supply multiple parameters as needed, such as Start(new { foo = nodeRef1, bar = nodeRef2 }).", true)]
        public ICypherFluentQuery AddStartPoint(string identity, params NodeReference[] nodeReferences)
        {
            throw new NotSupportedException();
        }

        [Obsolete("Call Start(new { foo = new[] { relRef1, relRef2 } }) instead. Supply multiple parameters as needed, such as Start(new { foo = nodeRef1, bar = nodeRef2 }).", true)]
        public ICypherFluentQuery AddStartPoint(string identity, params RelationshipReference[] relationshipReferences)
        {
            throw new NotSupportedException();
        }

        public ICypherFluentQuery Match(params string[] matchText)
        {
            return Mutate(w =>
                w.AppendClause("MATCH " + string.Join(", ", matchText)));
        }

        public ICypherFluentQuery Relate(string relateText)
        {
            if (Client.ServerVersion == new Version(1, 8) ||
                Client.ServerVersion >= new Version(1, 8, 0, 7))
                throw new NotSupportedException("You're trying to use the RELATE keyword against a Neo4j instance ≥ 1.8M07. In Neo4j 1.8M07, it was renamed from RELATE to CREATE UNIQUE. You need to update your code to use our new CreateUnique method. (We didn't want to just plumb the Relate method to CREATE UNIQUE, because that would introduce a deviation between the .NET wrapper and the Cypher language.)\r\n\r\nSee https://github.com/systay/community/commit/c7dbbb929abfef600266a20f065d760e7a1fff2e for detail.");

            return Mutate(w =>
                w.AppendClause("RELATE " + relateText));
        }

        public ICypherFluentQuery CreateUnique(string createUniqueText)
        {
            if (Client.ServerVersion < new Version(1, 8) ||
                (Client.ServerVersion >= new Version(1, 8, 0, 1) && Client.ServerVersion <= new Version(1, 8, 0, 6)))
                throw new NotSupportedException("The CREATE UNIQUE clause was only introduced in Neo4j 1.8M07, but you're querying against an older version of Neo4j. You'll want to upgrade Neo4j, or use the RELATE keyword instead. See https://github.com/systay/community/commit/c7dbbb929abfef600266a20f065d760e7a1fff2e for detail.");

            return Mutate(w =>
                w.AppendClause("CREATE UNIQUE " + createUniqueText));
        }

        public ICypherFluentQuery Create(string createText, params object[] objects)
        {
            objects
                .Select((o, i) =>
                {
                    if (o == null)
                        throw new ArgumentException("Array includes a null entry", "objects");

                    var objectType = o.GetType();
                    if (objectType.IsGenericType &&
                        objectType.GetGenericTypeDefinition() == typeof(Node<>))
                    {
                        throw new ArgumentException(string.Format(
                            "You're trying to pass in a Node<{0}> instance. Just pass the {0} instance instead.",
                            objectType.GetGenericArguments()[0].Name),
                            "objects");
                    }

                    var validationContext = new ValidationContext(o, null, null);
                    Validator.ValidateObject(o, validationContext);

                    var serializer = new CustomJsonSerializer { NullHandling = NullValueHandling.Ignore, QuoteName = false };
                    var objectText = serializer.Serialize(o);
                    return new KeyValuePair<string, string>("{" + i + "}", objectText);
                })
                .ToList()
                .ForEach(kv => createText = createText.Replace(kv.Key, kv.Value));

            return Mutate(w =>
                w.AppendClause("CREATE " + createText));
        }

        public ICypherFluentQuery Create<TNode>(string identity, TNode node)
            where TNode : class 
        {
            if (typeof(TNode).IsGenericType &&
                typeof(TNode).GetGenericTypeDefinition() == typeof(Node<>)) {
                throw new ArgumentException(string.Format(
                   "You're trying to pass in a Node<{0}> instance. Just pass the {0} instance instead.",
                   typeof(TNode).GetGenericArguments()[0].Name),
                   "node");
            }
            
            if (node == null)
                throw new ArgumentNullException("node");
            
            var validationContext = new ValidationContext(node, null, null);
            Validator.ValidateObject(node, validationContext);
            
            var serializer = new CustomJsonSerializer { NullHandling = NullValueHandling.Ignore, QuoteName = false};
            return Mutate(w =>
                w.AppendClause(string.Format("CREATE ({0} {1})", identity, serializer.Serialize(node))));
        }

        public ICypherFluentQuery Delete(string identities)
        {
            return Mutate(w =>
                w.AppendClause(string.Format("DELETE {0}", identities)));
        }

        public ICypherFluentQuery With(string withText)
        {
            return Mutate(w =>
                w.AppendClause(string.Format("WITH {0}", withText)));
        }

        public ICypherFluentQuery Set(string setText)
        {
            return Mutate(w =>
                w.AppendClause(string.Format("SET {0}", setText)));
        }

        public CypherQuery Query
        {
            get { return QueryWriter.ToCypherQuery(); }
        }

        public void ExecuteWithoutResults()
        {
            Client.ExecuteCypher(Query);
        }

        IGraphClient IAttachedReference.Client
        {
            get { return Client; }
        }
    }
}
