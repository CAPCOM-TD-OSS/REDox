using System;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public class Json5MutationGrowthTest
{
    private readonly ITestOutputHelper _output;

    public Json5MutationGrowthTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void RandomMutationDoesNotGrowStorageUnbounded(int seed)
    {
        const int Steps = 50000;
        const int Warmup = 5000;
        const int MaxItems = 16;

        var json5 = """
                    /* top */
                    {
                      // obj
                      obj: { a: 1, /* b */ b: 2, c: 3, }, // after obj
                      /* arr */ arr: [ 1, /* two */ 2, 3, ],
                    }
                    """;

        var options = new Json5DocumentOptions { PreserveTrivia = true };

        using var doc = Json5Document.Parse(json5, options: options);

        var random = new Random(seed);
        var nextKey = 0;
        var baselineTokens = 0;
        var baselineExtends = 0;
        var maxTokens = 0;
        var maxExtends = 0;

        for (var step = 0; step < Steps; step++)
        {
            var root = doc.RootElement.AsObject();
            var obj = root["obj"].AsObject();
            var arr = root["arr"].AsArray();

            switch (random.Next(6))
            {
                case 0 when obj.Count < MaxItems:
                    obj["k" + nextKey++ % 64] = random.Next(100);
                    break;
                case 1 when obj.Count > 0:
                    obj.Remove(obj.First().Key);
                    break;
                case 2 when arr.Count < MaxItems:
                    arr.Insert(random.Next(arr.Count + 1), new DObject { ["v"] = new DArray(random.Next(100)) });
                    break;
                case 3 when arr.Count > 0:
                    arr.RemoveAt(random.Next(arr.Count));
                    break;
                case 4 when arr.Count > 0:
                    arr[random.Next(arr.Count)].ReplaceWith(new DArray(1, 2));
                    break;
                case 5 when random.Next(16) == 0:
                    arr.Clear();
                    obj.Clear();
                    break;
            }

            if (step == Warmup)
            {
                baselineTokens = maxTokens;
                baselineExtends = maxExtends;
            }

            maxTokens = Math.Max(maxTokens, doc.GetTokens().Length);
            maxExtends = Math.Max(maxExtends, doc.GetExtends().Length);
        }

        _output.WriteLine($"seed={seed} baseline tokens={baselineTokens} extends={baselineExtends}");
        _output.WriteLine($"seed={seed} final    tokens={doc.GetTokens().Length} extends={doc.GetExtends().Length}");
        _output.WriteLine($"seed={seed} max      tokens={maxTokens} extends={maxExtends}");

        using var reparsed = Json5Document.Parse(
            Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions { PreserveTrivia = true }),
            options: options);

        Assert.True(maxTokens <= baselineTokens * 2,
            $"Token storage grew unbounded: baseline={baselineTokens}, max={maxTokens}");
        Assert.True(maxExtends <= baselineExtends * 2,
            $"Extend storage grew unbounded: baseline={baselineExtends}, max={maxExtends}");
    }
}