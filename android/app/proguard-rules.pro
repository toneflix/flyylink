# Keeps file and line numbers in crash reports; Play maps the names back with mapping.txt.
-keepattributes SourceFile,LineNumberTable
-renamesourcefileattribute SourceFile

# BouncyCastle refers to JNDI/LDAP classes that Android doesn't have; we only use its certificate builders.
-dontwarn javax.naming.**
-dontwarn org.bouncycastle.**
