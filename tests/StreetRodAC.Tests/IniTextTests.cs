using Street_Rod_AC.Parts.Export;

namespace StreetRodAC.Tests;

public class IniTextTests
{
    private const string Tyres =
        "; tyres of a test car\r\n" +
        "[HEADER]\r\n" +
        "VERSION=10\r\n" +
        "\r\n" +
        "[FRONT]\r\n" +
        "NAME=Street ; the first compound\r\n" +
        "WIDTH=0.205\r\n" +
        "\r\n" +
        "[REAR]\r\n" +
        "WIDTH=0.225\r\n" +
        "\r\n" +
        "[FRONT]\r\n" +
        "NAME=Semislick\r\n" +
        "WIDTH=0.215\r\n";

    [Fact]
    public void An_untouched_crlf_file_comes_back_identical()
    {
        var ini = new IniText(Tyres);
        Assert.Equal(Tyres, ini.ToString());
        Assert.False(ini.Changed);
    }

    [Fact]
    public void Lf_endings_become_crlf_and_trailing_blank_lines_go_as_documented()
    {
        Assert.Equal("[A]\r\nX=1\r\n", new IniText("[A]\nX=1\n\n\n").ToString());
    }

    [Fact]
    public void Get_reads_values_without_the_comment_and_by_occurrence()
    {
        var ini = new IniText(Tyres);
        Assert.Equal("Street", ini.Get("FRONT", "NAME"));
        Assert.Equal("Semislick", ini.Get("FRONT", "NAME", 1));
        Assert.Equal(0.215, ini.GetNumber("FRONT", "WIDTH", 1));
        Assert.Null(ini.Get("FRONT", "NAME", 2));
        Assert.Null(ini.Get("NOPE", "NAME"));
        Assert.Equal(["HEADER", "FRONT", "REAR", "FRONT"], ini.Sections);
    }

    [Fact]
    public void Set_keeps_the_comment_of_the_line()
    {
        var ini = new IniText(Tyres);
        ini.Set("FRONT", "NAME", "Race");
        Assert.Equal("Race", ini.Get("FRONT", "NAME"));
        Assert.Contains("NAME=Race\t\t\t; the first compound", ini.ToString());
        Assert.True(ini.Changed);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_number_that_is_not_finite_leaves_the_line_as_it_was(double value)
    {
        var ini = new IniText(Tyres);
        ini.Set("FRONT", "WIDTH", value, "0.000");
        Assert.Equal(Tyres, ini.ToString());
        Assert.False(ini.Changed);
    }

    [Fact]
    public void Numbers_are_written_invariant()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var ini = new IniText(Tyres);
            ini.Set("REAR", "WIDTH", 0.5, "0.000");
            Assert.Equal("0.500", ini.Get("REAR", "WIDTH"));
            Assert.True(ini.Scale("FRONT", "WIDTH", 2, "0.000", 1));
            Assert.Equal("0.430", ini.Get("FRONT", "WIDTH", 1));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Section_index_stays_right_after_lines_are_added_and_removed()
    {
        var ini = new IniText(Tyres);

        // A new key in the first FRONT moves every later header down one line
        ini.Set("FRONT", "DX_REF", "1.20");
        Assert.Equal("1.20", ini.Get("FRONT", "DX_REF"));
        Assert.Null(ini.Get("FRONT", "DX_REF", 1));
        Assert.Equal("Semislick", ini.Get("FRONT", "NAME", 1));

        // Removing REAR moves the second FRONT up
        ini.RemoveSection("REAR");
        Assert.Equal(["HEADER", "FRONT", "FRONT"], ini.Sections);
        Assert.Equal("0.215", ini.Get("FRONT", "WIDTH", 1));

        // A new section at the end, then a key in the second FRONT, then the new section is still found
        ini.Set("THERMAL_FRONT", "PERFORMANCE_CURVE", "tcurve.lut");
        ini.Set("FRONT", "PRESSURE_STATIC", "26", 1);
        Assert.Equal("26", ini.Get("FRONT", "PRESSURE_STATIC", 1));
        Assert.Equal("tcurve.lut", ini.Get("THERMAL_FRONT", "PERFORMANCE_CURVE"));

        ini.RemoveKey("FRONT", "WIDTH");
        Assert.Null(ini.Get("FRONT", "WIDTH"));
        Assert.Equal("0.215", ini.Get("FRONT", "WIDTH", 1));
        Assert.Equal("Semislick", ini.Get("FRONT", "NAME", 1));
    }

    [Fact]
    public void A_new_key_goes_before_the_blank_lines_that_end_its_section()
    {
        var ini = new IniText("[A]\r\nX=1\r\n\r\n[B]\r\nY=2\r\n");
        ini.Set("A", "Z", "3");
        Assert.Equal("[A]\r\nX=1\r\nZ=3\r\n\r\n[B]\r\nY=2\r\n", ini.ToString());
    }

    [Fact]
    public void Scale_leaves_a_missing_key_out_and_a_factor_of_one_alone()
    {
        var ini = new IniText("[A]\r\nX=1.50 ; kept as written\r\n");
        Assert.False(ini.Scale("A", "Y", 2, "0.00"));
        Assert.True(ini.Scale("A", "X", 1.0, "0.00"));
        Assert.Equal("[A]\r\nX=1.50 ; kept as written\r\n", ini.ToString());
    }

    [Fact]
    public void Null_or_empty_text_is_an_empty_file_that_can_be_written()
    {
        var ini = new IniText(null);
        Assert.Empty(ini.Sections);
        ini.Set("A", "X", "1");
        Assert.Equal("[A]\r\nX=1\r\n", ini.ToString());
    }
}
