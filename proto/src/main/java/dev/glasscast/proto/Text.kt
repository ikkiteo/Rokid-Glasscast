package dev.glasscast.proto

/** UI copy. Each app sets [en] once at startup; the Russian build leaves it false. */
object Text {
    @Volatile var en: Boolean = false

    fun t(ru: String, en: String): String = if (this.en) en else ru
}
