using Newtonsoft.Json;

namespace Legacy.NetFx48
{
    public static class Class1
    {
        public static string Echo(object value) => JsonConvert.SerializeObject(value);
    }
}
