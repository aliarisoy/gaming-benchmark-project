# Keep Kotlin serialization metadata for release builds.
-keepattributes *Annotation*, InnerClasses
-keep,includedescriptorclasses class com.rigsight.app.**$$serializer { *; }
-keepclassmembers class com.rigsight.app.** {
    *** Companion;
}
-keepclasseswithmembers class com.rigsight.app.** {
    kotlinx.serialization.KSerializer serializer(...);
}
