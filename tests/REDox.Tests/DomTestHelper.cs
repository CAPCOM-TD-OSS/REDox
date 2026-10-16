namespace REDox.Tests;

public static class DomTestHelper
{
    public static void DumpTokens(ITestOutputHelper output, Document doc)
    {
        output.WriteLine(doc.RootElement.ToString()!);

        var extends = doc.GetExtends();

        var index = 0;
        foreach (var token in doc.GetTokens())
        {
            if (token.IsExtended && !token.IsInlinePayload)
            {
                var extend = extends[(int)token.ExtendId];

                if (extend is DContainer container)
                {
                    output.Write(index + " : " + token + " extends: ");

                    output.Write("[");
                    for (var i = 0; i < container.Count; i++)
                    {
                        if (i > 0)
                        {
                            output.Write(",");
                        }

                        if (container is DArray)
                        {
                            output.Write(container.GetValueId(i).ToString()!);
                        }
                        else
                        {
                            output.Write("{");
                            output.Write(container.GetKeyId(i).ToString()!);
                            output.Write(",");
                            output.Write(container.GetValueId(i).ToString()!);
                            output.Write("}");
                        }
                    }

                    output.WriteLine("]");
                }
                else
                {
                    output.WriteLine(index + " : " + token + " extends: " + "\"" + extend + "\"");
                }
            }
            else
            {
                output.WriteLine(index + " : " + token);
            }

            index++;
        }
    }
}