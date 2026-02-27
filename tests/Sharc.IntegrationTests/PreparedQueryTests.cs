// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class PreparedQueryTests
{
    [Fact]
    public void Prepare_SimpleSelect_ReturnsPreparedQuery()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.Prepare("SELECT name, age FROM users");

        Assert.NotNull(prepared);
    }

    [Fact]
    public void Execute_SimpleSelect_ReturnsCorrectRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.Prepare("SELECT name, age FROM users");
        using var reader = prepared.Execute();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(10, count);
    }

    [Fact]
    public void Execute_MatchesQueryResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Collect results from Query()
        var queryNames = new List<string>();
        using (var reader = db.Query("SELECT name FROM users"))
        {
            while (reader.Read())
                queryNames.Add(reader.GetString(0));
        }

        // Collect results from Prepare().Execute()
        var preparedNames = new List<string>();
        using var prepared = db.Prepare("SELECT name FROM users");
        using (var reader = prepared.Execute())
        {
            while (reader.Read())
                preparedNames.Add(reader.GetString(0));
        }

        Assert.Equal(queryNames, preparedNames);
    }

    [Fact]
    public void Execute_WithFilter_ReturnsFilteredRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.Prepare("SELECT name FROM users WHERE age = 25");
        using var reader = prepared.Execute();

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Execute_MultipleTimes_ProducesConsistentResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.Prepare("SELECT name FROM users WHERE age > 25");

        for (int i = 0; i < 3; i++)
        {
            var names = new List<string>();
            using var reader = prepared.Execute();
            while (reader.Read())
                names.Add(reader.GetString(0));

            Assert.True(names.Count > 0, $"Iteration {i}: expected rows");
            // Each execution should produce same results
            if (i > 0)
            {
                var firstNames = new List<string>();
                using var firstReader = prepared.Execute();
                while (firstReader.Read())
                    firstNames.Add(firstReader.GetString(0));
                Assert.Equal(names, firstNames);
            }
        }
    }

    [Fact]
    public void Execute_WithParameterizedFilter_ReturnsCorrectResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.Prepare("SELECT name FROM users WHERE age = $targetAge");

        var parameters = new Dictionary<string, object> { ["targetAge"] = 25L };
        using var reader = prepared.Execute(parameters);

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Execute_DifferentParams_ReturnsDifferentResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.Prepare("SELECT name FROM users WHERE age = $targetAge");

        // First param set
        using var reader1 = prepared.Execute(new Dictionary<string, object> { ["targetAge"] = 25L });
        Assert.True(reader1.Read());
        var name1 = reader1.GetString(0);

        // Second param set — different value
        using var reader2 = prepared.Execute(new Dictionary<string, object> { ["targetAge"] = 23L });
        Assert.True(reader2.Read());
        var name2 = reader2.GetString(0);

        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void Prepare_CompoundQuery_ThrowsNotSupported()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Throws<NotSupportedException>(() =>
            db.Prepare("SELECT name FROM users UNION SELECT name FROM users"));
    }

    [Fact]
    public void Dispose_ThenExecute_ThrowsObjectDisposed()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var prepared = db.Prepare("SELECT name FROM users");
        prepared.Dispose();

        Assert.Throws<ObjectDisposedException>(() => prepared.Execute());
    }

    [Fact]
    public void Execute_ReusedParameterInstance_DoesNotRehashEveryCall()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.Prepare("SELECT name FROM users WHERE age = $targetAge");
        var parameters = new CountingParameters(("targetAge", 25L));

        using (var reader = prepared.Execute(parameters))
        {
            Assert.True(reader.Read());
        }

        using (var reader = prepared.Execute(parameters))
        {
            Assert.True(reader.Read());
        }

        Assert.Equal(1, parameters.EnumerationCount);
    }

    private sealed class CountingParameters : IReadOnlyDictionary<string, object>
    {
        private readonly Dictionary<string, object> _inner;

        public CountingParameters(params (string Key, object Value)[] entries)
        {
            _inner = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
                _inner[entries[i].Key] = entries[i].Value;
        }

        public int EnumerationCount { get; private set; }

        public int Count => _inner.Count;
        public IEnumerable<string> Keys => _inner.Keys;
        public IEnumerable<object> Values => _inner.Values;

        public object this[string key] => _inner[key];

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public bool TryGetValue(string key, out object value)
            => _inner.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            EnumerationCount++;
            return _inner.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}