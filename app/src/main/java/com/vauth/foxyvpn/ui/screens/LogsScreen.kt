package com.vauth.foxyvpn.ui.screens

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.DeleteSweep
import androidx.compose.material.icons.filled.Share
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.FileProvider
import com.vauth.foxyvpn.data.AppLogger
import com.vauth.foxyvpn.data.LogEntry
import com.vauth.foxyvpn.data.LogLevel
import com.vauth.foxyvpn.ui.theme.IosScreen
import com.vauth.foxyvpn.ui.theme.IosSegmented
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

private enum class LogFilter(val label: String, val levels: Set<LogLevel>) {
    ALL("All", setOf(LogLevel.INFO, LogLevel.WARN, LogLevel.ERROR)),
    INFO("Info", setOf(LogLevel.INFO)),
    PROBLEMS("Problems", setOf(LogLevel.WARN, LogLevel.ERROR)),
}

private suspend fun writeLogFile(context: Context): File = withContext(Dispatchers.IO) {
    val dir = File(context.cacheDir, "logs").apply { mkdirs() }
    val stamp = SimpleDateFormat("yyyyMMdd-HHmmss", Locale.US).format(Date())
    val file = File(dir, "foxyvpn-logs-$stamp.txt")
    file.writeText(AppLogger.exportAsText())
    file
}

@Composable
private fun NavIconButton(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    description: String,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .size(40.dp)
            .clip(CircleShape)
            .clickable(role = Role.Button, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            imageVector = icon,
            contentDescription = description,
            tint = MaterialTheme.colorScheme.primary,
            modifier = Modifier.size(22.dp),
        )
    }
}

@Composable
fun LogsScreen(onBack: () -> Unit) {
    val entries by AppLogger.entries.collectAsState()
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val timeFormat = remember { SimpleDateFormat("HH:mm:ss", Locale.US) }
    var filter by remember { mutableStateOf(LogFilter.ALL) }

    val countsByFilter: Map<LogFilter, Int> = remember(entries) {
        LogFilter.entries.associateWith { option -> entries.count { it.level in option.levels } }
    }
    val visible: List<LogEntry> = remember(entries, filter) {
        if (filter == LogFilter.ALL) entries else entries.filter { it.level in filter.levels }
    }
    val newestFirst: List<LogEntry> = remember(visible) { visible.asReversed() }

    IosScreen(
        title = "Logs",
        onBack = onBack,
        backLabel = "Settings",
        scrollable = false,
        actions = {
            NavIconButton(Icons.Filled.Share, "Save or share logs as a file") {
                if (entries.isEmpty()) {
                    Toast.makeText(context, "No log entries yet", Toast.LENGTH_SHORT).show()
                    return@NavIconButton
                }
                scope.launch {
                    try {
                        val file = writeLogFile(context)
                        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
                        val shareIntent = Intent(Intent.ACTION_SEND).apply {
                            type = "text/plain"
                            putExtra(Intent.EXTRA_STREAM, uri)
                            putExtra(Intent.EXTRA_SUBJECT, file.name)
                            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                        }
                        context.startActivity(
                            Intent.createChooser(shareIntent, "Save or send FoxyVPN logs").apply {
                                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                            },
                        )
                    } catch (cancellation: CancellationException) {
                        throw cancellation
                    } catch (failure: Exception) {
                        // A failed share (full storage, no handler app) must not take the app down.
                        Toast.makeText(context, "Could not share logs", Toast.LENGTH_SHORT).show()
                    }
                }
            }
            NavIconButton(Icons.Filled.ContentCopy, "Copy logs") {
                val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                clipboard.setPrimaryClip(ClipData.newPlainText("FoxyVPN logs", AppLogger.exportAsText()))
                Toast.makeText(context, "Logs copied", Toast.LENGTH_SHORT).show()
            }
            NavIconButton(Icons.Filled.DeleteSweep, "Clear logs") { AppLogger.clear() }
        },
    ) {
        val options = LogFilter.entries
        Box(Modifier.padding(start = 16.dp, end = 16.dp, bottom = 12.dp)) {
            IosSegmented(
                options = options.map { "${it.label} (${countsByFilter[it] ?: 0})" },
                selectedIndex = options.indexOf(filter),
                onSelect = { filter = options[it] },
            )
        }

        if (visible.isEmpty()) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
                Text(
                    text = when {
                        entries.isEmpty() -> "No log entries yet."
                        filter == LogFilter.PROBLEMS -> "No warnings or errors."
                        else -> "Nothing matches this filter."
                    },
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.padding(32.dp),
                )
            }
        } else {
            LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(start = 16.dp, end = 16.dp, bottom = 16.dp)
                    .clip(RoundedCornerShape(16.dp))
                    .background(MaterialTheme.colorScheme.surface),
            ) {
                items(newestFirst, key = { it.id }) { entry ->
                    val color = when (entry.level) {
                        LogLevel.ERROR -> MaterialTheme.colorScheme.error
                        LogLevel.WARN -> MaterialTheme.colorScheme.tertiary
                        LogLevel.INFO -> MaterialTheme.colorScheme.onSurfaceVariant
                    }
                    Column(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 7.dp)) {
                        Text(
                            text = "${timeFormat.format(Date(entry.timestampMillis))}  ${entry.level.name}  ${entry.tag}",
                            style = MaterialTheme.typography.labelSmall,
                            color = color,
                        )
                        Text(
                            text = entry.message,
                            style = MaterialTheme.typography.bodySmall,
                            fontFamily = FontFamily.Monospace,
                            fontSize = 12.sp,
                            color = MaterialTheme.colorScheme.onSurface,
                        )
                    }
                }
            }
        }
    }
}
