namespace GlassCast
{
    static class Lang
    {
#if EN
        public static bool En = true;
#else
        public static bool En = false;
#endif

        public static string T(string ru, string en)
        {
            return En ? en : ru;
        }

        public static string Ready { get { return T("готов", "ready"); } }
        public static string Stopped { get { return T("остановлено", "stopped"); } }
        public static string OnAir { get { return T("трансляция", "streaming"); } }

        public static string OnAirTo(string host)
        {
            return En ? "streaming to " + host : "трансляция на " + host;
        }
    }
}
