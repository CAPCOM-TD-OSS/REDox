using System;
using System.Globalization;
using Newtonsoft.Json;
using REDox.Json;

namespace REDox.Ini.Tests;

public class IniCompatibility
{
    private const string IniSample1 = """
                                      ; Application configuration
                                      # '#' style comments may also be supported

                                      application_name = SampleApp
                                      version = 1.0.0
                                      debug = false
                                      language = ja-JP

                                      [window]
                                      title = Sample Application
                                      width = 1920
                                      height = 1080
                                      fullscreen = false
                                      vsync = true

                                      [graphics]
                                      renderer = Vulkan
                                      quality = high
                                      anti_aliasing = 4
                                      shadow_quality = medium

                                      [audio]
                                      master_volume = 0.8
                                      music_volume = 0.6
                                      sound_volume = 0.9
                                      mute = false

                                      [network]
                                      host = example.com
                                      port = 443
                                      use_tls = true
                                      timeout = 30
                                      api_url = https://example.com/api/v1

                                      [database]
                                      server = localhost
                                      port = 5432
                                      database = sample_db
                                      username = user
                                      password = secret

                                      [paths]
                                      data = ./data
                                      cache = ./cache
                                      log = ./logs/application.log
                                      windows_path = C:\Program Files\SampleApp\data

                                      [user]
                                      name = John Doe
                                      email = john@example.com
                                      description = This is a value containing spaces.

                                      [optional]
                                      ; Empty value
                                      value =

                                      [special]
                                      ; '=' after the first separator is normally part of the value
                                      connection_string = Server=localhost;Database=test;User=user

                                      ; '#' and ';' may be treated as part of the value depending on the INI dialect
                                      color = #FF8800
                                      url = https://example.com/page#section
                                      command = foo;bar
                                      """;

    private const string IniSample2 = """
                                      ; ----- whitespace -----

                                         spaced_key   =   value with spaces   

                                      [  spaced section  ]
                                      key = value

                                      ; ----- empty values -----

                                      empty =
                                      whitespace_only =    

                                      ; ----- Unicode -----

                                      [日本語]
                                      名前 = REDox
                                      説明 = 日本語の設定値
                                      emoji = 🎮🔥

                                      ; ----- special characters -----

                                      [special]
                                      equals = a=b=c
                                      hash = value#fragment
                                      semicolon = value;data
                                      url = https://example.com/path?a=1&b=2#fragment

                                      ; ----- duplicate keys -----

                                      [array]
                                      item = first
                                      item = second
                                      item = third

                                      ; ----- duplicate name with different value types -----

                                      same = scalar

                                      [same]
                                      key = value
                                      """;

    private const string IniSample3 = """
                                      ; header comment 1
                                      # header comment 2


                                      global = value

                                      ; comment before section
                                      [first]
                                      ; comment immediately after section
                                      a = 1

                                      # comment between properties
                                      b = 2


                                      ; multiple comments
                                      ; before next section
                                      # another comment
                                      [second]

                                      ; comment before first property
                                      x = y

                                      ; comment after property
                                      z = value
                                      ; trailing comment
                                      """;

    private const string IniSample4 = """
                                      global = first
                                      global = second
                                      global = third

                                      [section]
                                      value = 1
                                      value = 2
                                      value = 3

                                      [section]
                                      value = 4

                                      [another]
                                      item = a
                                      item = b
                                      """;

    private const string IniSample5 = """
                                      same = scalar

                                      [same]
                                      key = value
                                      """;

    private const string IniSample6 = """
                                      日本語 = 日本語の値
                                      café = naïve
                                      emoji = 😀🎮🔥
                                      greek = αβγδε
                                      cyrillic = Привет
                                      korean = 안녕하세요
                                      chinese = 中文設定

                                      [日本語 セクション]
                                      名前 = REDox
                                      説明 = UTF-8のテストです
                                      絵文字 = 👨‍💻

                                      [café]
                                      résumé = français
                                      """;

    private const string IniSample7 = """
                                      [special-values]
                                      equals = a=b=c=d
                                      hash = value#fragment
                                      semicolon = value;fragment
                                      hash_start = #FF8800
                                      semicolon_start = ;not-comment
                                      brackets = [this is still a value]
                                      close_bracket = value]
                                      open_bracket = value[
                                      url = https://example.com/path?a=1&b=2#section
                                      query = foo=1&bar=2&baz=3
                                      windows_path = C:\Program Files\REDox\data
                                      unix_path = /usr/local/share/redox
                                      spaces = value with   multiple   spaces
                                      tabs = value	with	tabs
                                      """;

    private const string IniSample8 = """
                                         leading_key = value
                                      trailing_key    = value
                                      key with spaces = value
                                      key.with.dots = value
                                      key-with-dashes = value
                                      key_with_underscores = value
                                      key[0] = value

                                      [ section ]
                                      key = value

                                      [section with spaces]
                                      key with spaces = value

                                      [section.with.dots]
                                      dotted.key = dotted value

                                      [section-with-dashes]
                                      key = value
                                      """;

    private const string IniSample9 = """
                                      global_empty =
                                      global_value = value

                                      [empty-section]

                                      [values]
                                      empty =
                                      whitespace =    
                                      normal = value

                                      [another-empty-section]

                                      [last]
                                      value = end
                                      """;

    private const string IniSample10 = """
                                          first    =    one
                                       second=two
                                       third =three
                                       fourth= four

                                       [  section  ]
                                       a    =    1
                                       b=2
                                       c =3
                                       d= 4
                                       """;

    [Fact]
    public void DomToIni()
    {
        var obj = new { Name = "MyName", Age = 26, Address = new { Street = "MyStreet", City = "MyCity" } };

        var value = DValue.From(obj);

        var ini = IniDocument.EncodeToString(value);

        TestContext.Current.TestOutputHelper?.WriteLine(ini);
    }

    [Theory]
    [InlineData(false, 0, "2026-10-04T12:34:56")]
    [InlineData(true, 0, "2026-10-04T12:34:56")]
    [InlineData(false, 123, "2026-10-04T12:34:56.123")]
    [InlineData(true, 123, "2026-10-04T12:34:56.123")]
    public void EncodeToStringWithDateTime(bool inSection, int milliseconds, string expectedTimestamp)
    {
        using var doc = IniDocument.Parse(inSection ? "[dates]\nDate=\n" : "Date=\n");
        var root = doc.RootElement.AsObject();
        var properties = inSection ? root["dates"].AsObject() : root;
        var date = new DateTime(2026, 10, 4, 12, 34, 56, milliseconds, DateTimeKind.Unspecified);

        // Store a timestamp value in the parsed document, rather than a formatted string.
        properties["Date"] = date;
        Assert.Equal(DTokenKind.Timestamp, properties["Date"].GetToken().Kind);

        var ini = IniDocument.EncodeToString(doc.RootElement, new IniWriteOptions { NewLine = "\n" });

        Assert.Equal((inSection ? "[dates]\n" : string.Empty) + "Date=" + expectedTimestamp, ini);

        using var reparsed = IniDocument.Parse(ini);
        var parsedProperties = inSection ? reparsed.RootElement.GetProperty("dates") : reparsed.RootElement;
        Assert.Equal(expectedTimestamp, parsedProperties.GetProperty("Date").GetString());
    }

    [Fact]
    public void EncodeToStringWithDateTimeUsesFormatAndCulture()
    {
        var settings = new DoxSerializerSettings
        {
            DateFormatString = "dd MMMM yyyy HH:mm:ss.FFFFFFF",
            Culture = CultureInfo.GetCultureInfo("fr-FR"),
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat
        };
        using var doc = IniDocument.Parse("[dates]\nDate=\n", settings);
        doc.RootElement.AsObject()["dates"].AsObject()["Date"] =
            new DateTime(2026, 10, 4, 12, 34, 56).AddTicks(1234567);

        var ini = IniDocument.EncodeToString(doc.RootElement, new IniWriteOptions { NewLine = "\n" });

        Assert.Equal("[dates]\nDate=04 octobre 2026 12:34:56.1234567", ini);
        using var reparsed = IniDocument.Parse(ini);
        Assert.Equal("04 octobre 2026 12:34:56.1234567",
            reparsed.RootElement.GetProperty("dates").GetProperty("Date").GetString());
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.RoundtripKind)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.RoundtripKind)]
    [InlineData(DateTimeKind.Local, Serialization.DateTimeZoneHandling.RoundtripKind)]
    [InlineData(DateTimeKind.Local, Serialization.DateTimeZoneHandling.Utc)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.Local)]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.Utc)]
    [InlineData(DateTimeKind.Unspecified, Serialization.DateTimeZoneHandling.Local)]
    [InlineData(DateTimeKind.Utc, Serialization.DateTimeZoneHandling.Unspecified)]
    public void EncodeToStringWithDateTimeHonorsZoneHandling(DateTimeKind kind, Serialization.DateTimeZoneHandling zone)
    {
        var settings = new DoxSerializerSettings { DateTimeZoneHandling = zone };
        var date = new DateTime(2026, 10, 4, 12, 34, 56, kind).AddTicks(1);
        using var doc = IniDocument.Parse("Date=\n", settings);
        doc.RootElement.AsObject()["Date"] = date;

        var json = JsonConvert.SerializeObject(date, new JsonSerializerSettings
        {
            DateTimeZoneHandling = (DateTimeZoneHandling)zone
        });
        var expected = "Date=" + JsonConvert.DeserializeObject<string>(json);

        Assert.Equal(expected, IniDocument.EncodeToString(doc.RootElement));
        // Typed serialization and direct DOM insertion must produce the same timestamp.
        Assert.Equal(expected, IniDocument.EncodeToString(DValue.From(new { Date = date }, settings)));
    }

    [Fact]
    public void EncodeToStringWithDateTimeUsesMicrosoftFormat()
    {
        using var doc = IniDocument.Parse("Date=\n", new DoxSerializerSettings
        {
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat
        });
        doc.RootElement.AsObject()["Date"] = DateTime.UnixEpoch;

        Assert.Equal("Date=/Date(0)/", IniDocument.EncodeToString(doc.RootElement));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(65)]
    public void EncodeToStringWithDateFormatAtBufferBoundary(int length)
    {
        var literal = new string('x', length);
        using var doc = IniDocument.Parse("Date=\n", new DoxSerializerSettings
        {
            DateFormatString = "'" + literal + "'"
        });
        doc.RootElement.AsObject()["Date"] =
            new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc).AddTicks(1);

        // A custom value that fits is preserved; overflow must produce an ISO timestamp.
        var expected = length == 64 ? literal : "2026-10-04T12:34:56.0000001Z";
        Assert.Equal("Date=" + expected, IniDocument.EncodeToString(doc.RootElement));
    }

    [Fact]
    public void EncodeToStringWithLongDateFormatFallsBackToIso()
    {
        using var doc = IniDocument.Parse("[dates]\nDate=\n", new DoxSerializerSettings
        {
            DateFormatString = "yyyy '" + new string('あ', 80) + "'",
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat,
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Utc
        });
        doc.RootElement.AsObject()["dates"].AsObject()["Date"] =
            new DateTime(2026, 10, 4, 12, 34, 56).AddTicks(1);

        Assert.Equal("[dates]\nDate=2026-10-04T12:34:56.0000001Z",
            IniDocument.EncodeToString(doc.RootElement, new IniWriteOptions { NewLine = "\n" }));
    }

    [Theory]
    [InlineData(TimestampKind.LocalDate, "2026-10-04")]
    [InlineData(TimestampKind.LocalTime, "12:34:56.0000001")]
    [InlineData(TimestampKind.LocalDateTime, "2026-10-04T12:34:56.0000001")]
    [InlineData(TimestampKind.OffsetDateTime, "2026-10-04T12:34:56.0000001+00:00")]
    public void EncodeToStringWithExplicitTimestampKindUsesIso(TimestampKind kind, string expected)
    {
        using var doc = IniDocument.Parse("Date=\n", new DoxSerializerSettings
        {
            Culture = CultureInfo.GetCultureInfo("fr-FR"),
            DateFormatString = "dddd",
            DateFormatHandling = Serialization.DateFormatHandling.MicrosoftDateFormat,
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Local
        });
        doc.RootElement.AsObject()["Date"] =
            DValue.Create(new DateTime(2026, 10, 4, 12, 34, 56, DateTimeKind.Utc).AddTicks(1), kind);

        Assert.Equal(kind, doc.RootElement.GetProperty("Date").AsValue().GetToken().TimestampKind);
        Assert.Equal("Date=" + expected, IniDocument.EncodeToString(doc.RootElement));
    }

    [Fact]
    public void EncodeToStringWithDateTimeOffsetPreservesOffset()
    {
        using var doc = IniDocument.Parse("Date=\n", new DoxSerializerSettings
        {
            DateFormatString = "yyyy",
            DateTimeZoneHandling = Serialization.DateTimeZoneHandling.Utc
        });
        doc.RootElement.AsObject()["Date"] =
            new DateTimeOffset(2026, 10, 4, 12, 34, 56, TimeSpan.FromMinutes(330)).AddTicks(1234567);

        Assert.Equal("Date=2026-10-04T12:34:56.1234567+05:30", IniDocument.EncodeToString(doc.RootElement));
    }

    [Fact]
    public void EncodeToStringWithInheritedTimestampKindPreservesDateOnly()
    {
        using var doc = IniDocument.Parse("Date=\n", new DoxSerializerSettings { DateFormatString = "yyyy" });
        var properties = doc.RootElement.AsObject();
        properties["Date"] = DValue.Create(new DateTime(2025, 1, 1), TimestampKind.LocalDate);
        properties["Date"] = new DateTime(2026, 10, 4, 12, 34, 56);

        Assert.Equal(TimestampKind.LocalDate, properties["Date"].GetToken().TimestampKind);
        Assert.Equal("Date=2026-10-04", IniDocument.EncodeToString(doc.RootElement));
    }

    [Theory]
    [InlineData(IniSample1)]
    [InlineData(IniSample2)]
    [InlineData(IniSample3)]
    [InlineData(IniSample4)]
    [InlineData(IniSample5)]
    [InlineData(IniSample6)]
    [InlineData(IniSample7)]
    [InlineData(IniSample8)]
    [InlineData(IniSample9)]
    [InlineData(IniSample10)]
    public void RoundTripIni(string ini)
    {
        using var doc = IniDocument.Parse(ini,
            options: new IniDocumentOptions { PreserveTrivia = true, AllowDuplicateKeys = true });

        var result = IniDocument.EncodeToString(doc.RootElement, new IniWriteOptions { PreserveTrivia = true });

        Assert.Equal(ini, result);
    }

    [Theory]
    [InlineData(IniSample1)]
    [InlineData(IniSample2)]
    [InlineData(IniSample3)]
    [InlineData(IniSample4)]
    [InlineData(IniSample5)]
    [InlineData(IniSample6)]
    [InlineData(IniSample7)]
    [InlineData(IniSample8)]
    [InlineData(IniSample9)]
    [InlineData(IniSample10)]
    public void RoundTripIni2(string ini)
    {
        using var doc = IniDocument.Parse(ini,
            options: new IniDocumentOptions { PreserveTrivia = true, AllowDuplicateKeys = true });

        var result1 = IniDocument.EncodeToString(doc.RootElement);

        using var doc2 = IniDocument.Parse(result1,
            options: new IniDocumentOptions { PreserveTrivia = true, AllowDuplicateKeys = true });

        Assert.Equal(doc.RootElement.ToJsonString(), doc2.RootElement.ToJsonString());

        var result2 = IniDocument.EncodeToString(doc2.RootElement);

        Assert.Equal(result1, result2);
    }
}